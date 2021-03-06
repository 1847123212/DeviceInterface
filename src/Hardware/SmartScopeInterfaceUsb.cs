﻿using LabNation.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace LabNation.DeviceInterface.Hardware
{
    public class SmartScopeInterfaceUsb : ISmartScopeInterface
    {
        public enum PIC_COMMANDS
        {
            PIC_VERSION = 1,
            PIC_WRITE = 2,
            PIC_READ = 3,
            PIC_RESET = 4,
            PIC_BOOTLOADER = 5,
            EEPROM_READ = 6,
            EEPROM_WRITE = 7,
            FLASH_ROM_READ = 8,
            FLASH_ROM_WRITE = 9,
            I2C_WRITE = 10,
            I2C_READ = 11,
            PROGRAM_FPGA_START = 12,
            PROGRAM_FPGA_END = 13,
            I2C_WRITE_START = 14,
            I2C_WRITE_BULK = 15,
            I2C_WRITE_STOP = 16,
        }

        public static byte HEADER_CMD_BYTE = 0xC0; //C0 as in Command
        public static byte HEADER_RESPONSE_BYTE = 0xAD; //AD as in Answer Dude
        public static int FLASH_USER_ADDRESS_MASK = 0x0FFF;
        public static byte FPGA_I2C_ADDRESS_AWG = 0x0E;
        public static int I2C_MAX_WRITE_LENGTH = 27;
        public static int I2C_MAX_WRITE_LENGTH_BULK = 29;

        public enum Operation { READ, WRITE, WRITE_BEGIN, WRITE_BODY, WRITE_END };

        internal ISmartScopeHardwareUsb usb;
        public SmartScopeInterfaceUsb(ISmartScopeHardwareUsb hwInterface)
        {
            this.usb = hwInterface;
        }

        public string Serial { get { return usb.Serial; } }
        public void Destroy() { usb.Destroy(); }
        public bool Destroyed { get { return usb.Destroyed;  } }
        public void GetControllerRegister(ScopeController ctrl, uint address, uint length, out byte[] data)
        {
            //In case of FPGA (I2C), first write address we're gonna read from to FPGA
            //FIXME: this should be handled by the PIC firmware
            if (ctrl == ScopeController.FPGA)
                SetControllerRegister(ctrl, address, null);

            if (ctrl == ScopeController.FLASH && (address + length) > (FLASH_USER_ADDRESS_MASK + 1))
            {
                throw new ScopeIOException(String.Format("Can't read flash rom beyond 0x{0:X8}", FLASH_USER_ADDRESS_MASK));
            }

            byte[] header = UsbCommandHeader(ctrl, Operation.READ, address, length);
            usb.WriteControlBytes(header, false);

            //EP3 always contains 16 bytes xxx should be linked to constant
            //FIXME: use endpoint length or so, or don't pass the argument to the function
            byte[] readback = ReadControlBytes(16);
            if(readback == null)
            {
                data = null;
                LabNation.Common.Logger.Error("Failde to read back bytes");
                return;
            }


            int readHeaderLength;
            if (ctrl == ScopeController.FLASH)
                readHeaderLength = 5;
            else
                readHeaderLength = 4;

            //strip away first 4 bytes as these are not data
            data = new byte[length];
            Array.Copy(readback, readHeaderLength, data, 0, length);
        }
        public void SetControllerRegister(ScopeController ctrl, uint address, byte[] data)
        {
            if (data != null && data.Length > I2C_MAX_WRITE_LENGTH)
            {
                int offset = 0;

                if (ctrl != ScopeController.AWG)
                {
                    //Chop up in smaller chunks
                    int bytesLeft = data.Length;
                    
                    while(bytesLeft > 0)
                    {
                        int length = bytesLeft > I2C_MAX_WRITE_LENGTH ? I2C_MAX_WRITE_LENGTH : bytesLeft;
                        byte[] newData = new byte[length];
                        Array.Copy(data, offset, newData, 0, length);
                        SetControllerRegister(ctrl, (uint)(address + offset), newData);
                        offset += length;
                        bytesLeft -= length;
                    }
                    return;
                }

                byte[] toSend = new byte[32];

                //Begin I2C - send start condition
                usb.WriteControlBytes(UsbCommandHeader(ctrl, Operation.WRITE_BEGIN, address, 0), false);

                while (offset < data.Length)
                {
                    int length = Math.Min(data.Length - offset, I2C_MAX_WRITE_LENGTH_BULK);
                    byte[] header = UsbCommandHeader(ctrl, Operation.WRITE_BODY, address, (uint)length);
                    Array.Copy(header, toSend, header.Length);
                    Array.Copy(data, offset, toSend, header.Length, length);
                    usb.WriteControlBytes(toSend, false);
                    offset += length;
                }
                usb.WriteControlBytes(UsbCommandHeader(ctrl, Operation.WRITE_END, address, 0), false);
            }
            else
            {
                uint length = data != null ? (uint)data.Length : 0;
                byte[] header = UsbCommandHeader(ctrl, Operation.WRITE, address, length);

                //Paste header and data together and send it
                byte[] toSend = new byte[header.Length + length];
                Array.Copy(header, toSend, header.Length);
                if (length > 0)
                    Array.Copy(data, 0, toSend, header.Length, data.Length);
                usb.WriteControlBytes(toSend, false);
            }
        }
        public void FlushDataPipe()
        {
            usb.FlushDataPipe();
        }
        public void SendCommand(PIC_COMMANDS cmd, bool async = false)
        {
            byte[] toSend = new byte[2] { HEADER_CMD_BYTE, (byte)cmd };
            usb.WriteControlBytes(toSend, async);
        }
        public void Reset()
        {
            SendCommand(PIC_COMMANDS.PIC_RESET, true);
#if IOS
			Common.Logger.Debug("Destroying interface after reset for ios");
            Destroy();
#endif
        }
        public byte[] PicFirmwareVersion
        {
            get
            {
                SendCommand(PIC_COMMANDS.PIC_VERSION);
                byte[] response = ReadControlBytes(16);
                return response.Skip(4).Take(3).Reverse().ToArray();
            }
        }

        public byte[] GetData(int length)
        {
            byte[] buffer = new byte[length];
            usb.GetData(buffer, 0, length);
            return buffer;
        }

        public int GetAcquisition(byte[] buffer)
        {
            return usb.GetAcquisition(buffer);
        }

        public bool FlashFpga(byte[] firmware)
        {
            int packetSize = 32;
            int packetsPerCommand = 64;
            int padding = 2048 / 8;

            //Data to send to keep clock running after all data was sent
            byte[] dummyData = new byte[packetSize];
            for (int i = 0; i < dummyData.Length; i++)
                dummyData[i] = 255;

            //Send FW to FPGA
            try
            {
                Stopwatch flashStopwatch = new Stopwatch();
                flashStopwatch.Start();
                UInt16 commands = (UInt16)(firmware.Length / packetSize + padding);
                //PIC: enter FPGA flashing mode
                byte[] msg = new byte[] {
                    SmartScopeInterfaceUsb.HEADER_CMD_BYTE,
                    (byte)SmartScopeInterfaceUsb.PIC_COMMANDS.PROGRAM_FPGA_START,
                    (byte) (commands >> 8),
                    (byte) (commands),
                };
                usb.WriteControlBytes(msg, false);

                //FIXME: this sleep is found necessary on android tablets.
                /* The problem occurs when a scope is initialised the *2nd*
                 * time after the app starts, i.e. after replugging it.
                 * A possible explanation is that in the second run, caches
                 * are hit and the time between the PROGRAM_FPGA_START command
                 * and the first bitstream bytes is smaller than on the first run.
                 * 
                 * Indeed, if this time is smaller than the time for the INIT bit
                 * (see spartan 6 ug380 fig 2.4) to rise, the first bitstream data
                 * is missed and the configuration fails.
                 */
                System.Threading.Thread.Sleep(10);
                usb.FlushDataPipe();

                int bytesSent = 0;
                int commandSize = packetsPerCommand * packetSize;
                while (bytesSent < firmware.Length)
                {
                    if (bytesSent + commandSize > firmware.Length)
                        commandSize = firmware.Length - bytesSent;
                    usb.WriteControlBytesBulk(firmware, bytesSent, commandSize, false);
                    bytesSent += commandSize;
                }
                flashStopwatch.Stop();
                for (int j = 0; j < padding; j++)
                {
                    usb.WriteControlBytesBulk(dummyData, false);
                }

                //Send finish flashing command
                SendCommand(SmartScopeInterfaceUsb.PIC_COMMANDS.PROGRAM_FPGA_END);
                Logger.Debug(String.Format("Flashed FPGA in {0:0.00}s", (double)flashStopwatch.ElapsedMilliseconds / 1000.0));
                Logger.Debug("Flushing data pipe");
                //Flush whatever might be left in the datapipe
                usb.FlushDataPipe();
            }
            catch (ScopeIOException e)
            {
                Logger.Error("Flashing FPGA failed failed");
                Logger.Error(e.Message);
                return false;
            }
            return true;
        }

        public void LoadBootLoader()
        {
            SendCommand(PIC_COMMANDS.PIC_BOOTLOADER, true);
        }
        private byte[] ReadControlBytes(int length)
        {
            byte[] buffer = new byte[length];
            usb.ReadControlBytes(buffer, 0, length);
            return buffer;
        }
        private static byte[] UsbCommandHeader(ScopeController ctrl, Operation op, uint address, uint length)
        {
            byte[] header = null;

            if (ctrl == ScopeController.PIC)
            {
                if (op == Operation.WRITE)
                {
                    header = new byte[4] {
                               HEADER_CMD_BYTE,
               (byte)PIC_COMMANDS.PIC_WRITE, 
                            (byte)(address),
                             (byte)(length)  //first I2C byte: FPGA i2c address (5) + '0' as LSB, indicating write operation
                        };
                }
                else if (op == Operation.READ)
                {
                    header = new byte[4] {
                               HEADER_CMD_BYTE,
                (byte)PIC_COMMANDS.PIC_READ, 
                            (byte)(address),
                             (byte)(length)  //first I2C byte: FPGA i2c address (5) + '0' as LSB, indicating write operation
                        };
                }
            }
            else if (ctrl == ScopeController.ROM)
            {
                if (op == Operation.WRITE)
                {
                    header = new byte[4] {
                               HEADER_CMD_BYTE,
            (byte)PIC_COMMANDS.EEPROM_WRITE, 
                            (byte)(address),
                             (byte)(length)
                        };
                }
                else if (op == Operation.READ)
                {
                    header = new byte[4] {
                               HEADER_CMD_BYTE,
             (byte)PIC_COMMANDS.EEPROM_READ, 
                            (byte)(address),
                             (byte)(length)
                        };
                }
            }
            else if (ctrl == ScopeController.FLASH)
            {
                if (op == Operation.WRITE)
                {
                    header = new byte[5] {
                               HEADER_CMD_BYTE,
         (byte)PIC_COMMANDS.FLASH_ROM_WRITE, 
                            (byte)(address),
                             (byte)(length),
                       (byte)(address >> 8),
                        };
                }
                else if (op == Operation.READ)
                {
                    header = new byte[5] {
                               HEADER_CMD_BYTE,
          (byte)PIC_COMMANDS.FLASH_ROM_READ, 
                            (byte)(address),
                             (byte)(length),
                       (byte)(address >> 8),
                        };
                }
            }
            else if (ctrl == ScopeController.FPGA) // Generic FPGA I2C operation
            {
                //Address contains both device address and register address
                // A[0:7] = reg addr
                // A[7:14] = device address
                header = UsbCommandHeaderI2c((byte)((address >> 8) & 0x7F), op, (byte)(address & 0xFF), length);
            }
            else if (ctrl == ScopeController.AWG)
            {
                if (op == Operation.WRITE)
                {
                    header = UsbCommandHeaderI2c(FPGA_I2C_ADDRESS_AWG, op, address, length);
                }
                if (op == Operation.WRITE_BEGIN)
                {
                    header = new byte[5] {
                            HEADER_CMD_BYTE,
         (byte)PIC_COMMANDS.I2C_WRITE_START,
                         (byte)(length + 2), //data and 2 more bytes: the FPGA I2C address, and the register address inside the FPGA
          (byte)(FPGA_I2C_ADDRESS_AWG << 1), //first I2C byte: FPGA i2c address bit shifted and LSB 0 indicating write
                              (byte)address  //second I2C byte: address of the register inside the FPGA
                    };
                }
                if (op == Operation.WRITE_BODY)
                {
                    header = new byte[3] {
                               HEADER_CMD_BYTE,
             (byte)PIC_COMMANDS.I2C_WRITE_BULK,
                                (byte)(length), //data and 2 more bytes: the FPGA I2C address, and the register address inside the FPGA
                    };
                }
                if (op == Operation.WRITE_END)
                {
                    header = new byte[3] {
                               HEADER_CMD_BYTE,
             (byte)PIC_COMMANDS.I2C_WRITE_STOP,
                             (byte)(length)
                    };
                }
                else if (op == Operation.READ)
                {
                    throw new Exception("Can't read out AWG");
                }
            }
            return header;
        }
        private static byte[] UsbCommandHeaderI2c(byte I2cAddress, Operation op, uint address, uint length)
        {
            byte[] header;
            if (op == Operation.WRITE)
            {
                header = new byte[5] {
                               HEADER_CMD_BYTE,
               (byte)PIC_COMMANDS.I2C_WRITE,
                         (byte)(length + 2), //data and 2 more bytes: the FPGA I2C address, and the register address inside the FPGA
     (byte)(I2cAddress << 1), //first I2C byte: FPGA i2c address bit shifted and LSB 0 indicating write
                              (byte)address  //second I2C byte: address of the register inside the FPGA
                    };
            }
            else if (op == Operation.READ)
            {
                header = new byte[4] {
                               HEADER_CMD_BYTE,
                (byte)PIC_COMMANDS.I2C_READ,
          (byte)(I2cAddress), //first I2C byte: FPGA i2c address bit shifted and LSB 1 indicating read
                             (byte)(length) 
                    };
            }
            else 
            {
                throw new Exception("Unsupported operation for I2C Header");
            }
            return header;
        }

    }
}
