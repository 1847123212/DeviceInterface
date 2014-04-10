﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ECore.DeviceMemories;
using ECore.DataPackages;
using System.IO;
#if IPHONE || ANDROID
#else
using System.Windows.Forms;
#endif


namespace ECore.DeviceImplementations
{
    //this is the main class which fills the EDevice with data specific to the HW implementation.
    //eg: which memories, which registers in these memories, which additional functionalities, the start and stop routines, ...
    public partial class ScopeV2: EDeviceImplementation, IScope
    {
        
		public static string DemoStatusText = "";
        public DeviceMemories.ScopeFpgaSettingsMemory  FpgaSettingsMemory { get; private set; }
        public DeviceMemories.ScopeFpgaRom FpgaRom { get; private set; }
        public DeviceMemories.ScopeStrobeMemory StrobeMemory { get; private set; }
        public DeviceMemories.MAX19506Memory AdcMemory { get; private set; }
        public DeviceMemories.ScopePicRegisterMemory PicMemory { get; private set; }
        private ScopeV2RomManager romManager;
        
        private float[] calibrationCoefficients = new float[] {0.0042f, -0.0029f, 0.1028f};
        private int yOffset_Midrange0V;
        public float ChannelAYOffsetVoltage { get { return 0; } }
        public float ChannelBYOffsetVoltage { get { return (float)((FpgaSettingsMemory.GetRegister(REG.CHB_YOFFSET_VOLTAGE).InternalValue-yOffset_Midrange0V)) * calibrationCoefficients[1]; } }
        private bool disableVoltageConversion;
        private const double SAMPLE_PERIOD = 10e-9;

		#if ANDROID
		public Android.Content.Res.AssetManager Assets;
		#endif
        
        public ScopeV2(EDevice eDevice) : base(eDevice) 
        {
            //figure out which yOffset value needs to be put in order to set a 0V signal to midrange of the ADC = 128binary
            //FIXME: no clue why this line is here...
            yOffset_Midrange0V = (int)((0 - 128f * calibrationCoefficients[0] - calibrationCoefficients[2]) / calibrationCoefficients[1]);
        }

        #region initializers

        public override void InitializeHardwareInterface()
        {
			#if ANDROID
			hardwareInterface = new HardwareInterfaces.HWInterfacePIC_Xamarin(this);
			#else
			hardwareInterface = new HardwareInterfaces.HWInterfacePIC_LibUSB();

			//check communication by reading PIC FW version
            if(hardwareInterface.Connected)
            {
                hardwareInterface.WriteControlBytes(new byte[] { 123, 1 });
                byte[] response = hardwareInterface.ReadControlBytes(16);
                string resultString = "PIC FW Version readout (" + response.Length.ToString() + " bytes): ";
                foreach (byte b in response)
                    resultString += b.ToString() + ";";
                Logger.AddEntry(this, LogMessageType.Persistent, resultString);
            }
			#endif
        }

        public ScopeV2RomManager CreateRomManager()
        {
            return new ScopeV2RomManager(eDevice);
        }

        //master method where all memories, registers etc get defined and linked together
        public override void InitializeMemories()
        {
            //Create memories
            PicMemory = new DeviceMemories.ScopePicRegisterMemory(eDevice);
            FpgaSettingsMemory = new DeviceMemories.ScopeFpgaSettingsMemory(eDevice);
            FpgaRom = new DeviceMemories.ScopeFpgaRom(eDevice);
            StrobeMemory = new DeviceMemories.ScopeStrobeMemory(eDevice, FpgaSettingsMemory, FpgaRom);
            AdcMemory = new DeviceMemories.MAX19506Memory(eDevice, FpgaSettingsMemory, StrobeMemory, FpgaRom);
            //Add them in order we'd like them in the GUI
            
            byteMemories.Add(FpgaRom);
            byteMemories.Add(FpgaSettingsMemory);
            byteMemories.Add(AdcMemory);
            byteMemories.Add(PicMemory);
            byteMemories.Add(StrobeMemory);

            this.romManager = CreateRomManager();
        }

        public override void InitializeDataSources()
        {
            dataSources.Add(new DataSources.DataSourceScope(this));
        }

        #endregion

        #region start_stop

        override public bool Start()
        {
            if (!hardwareInterface.Start())
                return false;
            
            //raise global reset
            StrobeMemory.GetRegister(STR.GLOBAL_RESET).InternalValue = 1;
            StrobeMemory.WriteSingle(STR.GLOBAL_RESET);

            //flush any transfers still queued on PIC
            //eDevice.HWInterface.FlushHW();

            //set feedback loopand to 1V for demo purpose and enable
            this.SetDivider(0, 1);
            this.SetDivider(1, 1);

            FpgaSettingsMemory.GetRegister(REG.CALIB_VOLTAGE).InternalValue = 78;
            FpgaSettingsMemory.WriteSingle(REG.CALIB_VOLTAGE);

            //FIXME: use this instead of code below
            //this.SetTriggerLevel(0f);
            //FpgaSettingsMemory.GetRegister(REG.TRIGGERLEVEL).InternalValue = 130;
            //FpgaSettingsMemory.WriteSingle(REG.TRIGGERLEVEL);

            //FIXME: these are byte values, since the setter helper is not converting volt to byte
            this.SetYOffset(0, 100f);
            this.SetYOffset(1, 100f);

            //fpgaMemory.RegisterByName(REG.TRIGGERHOLDOFF_B1).InternalValue = 4;
            //fpgaMemory.WriteSingle(REG.TRIGGERHOLDOFF_B1);

            //fpgaMemory.RegisterByName(REG.SAMPLECLOCKDIV_B1).InternalValue = 1;
            //fpgaMemory.WriteSingle(REG.SAMPLECLOCKDIV_B1);

            //fpgaMemory.RegisterByName(REG.SAMPLECLOCKDIV_B1).InternalValue = 1;
            //fpgaMemory.WriteSingle(REG.SAMPLECLOCKDIV_B1);

            //set ADC output resistance to 300ohm instead of 50ohm
            AdcMemory.GetRegister(MAX19506.CHA_TERMINATION).InternalValue = 4;
            AdcMemory.WriteSingle(MAX19506.CHA_TERMINATION);
            AdcMemory.GetRegister(MAX19506.CHB_TERMINATION).InternalValue = 4;
            AdcMemory.WriteSingle(MAX19506.CHB_TERMINATION);

            //set ADC to offset binary output (required for FPGA triggering)
            AdcMemory.GetRegister(MAX19506.FORMAT_PATTERN).InternalValue = 16;
            AdcMemory.WriteSingle(MAX19506.FORMAT_PATTERN);

            this.SetEnableDcCoupling(0, true);
            this.SetEnableDcCoupling(1, true);

            //this.SetEnableFreeRunning(true);

            //Set ADC multiplexed output mode
            AdcMemory.GetRegister(MAX19506.OUTPUT_FORMAT).Set(0x02);
            AdcMemory.WriteSingle(MAX19506.OUTPUT_FORMAT);
            AdcMemory.GetRegister(MAX19506.CHA_TERMINATION).Set(27);
            AdcMemory.WriteSingle(MAX19506.CHA_TERMINATION);
            AdcMemory.GetRegister(MAX19506.DATA_CLK_TIMING).Set(24);
            AdcMemory.WriteSingle(MAX19506.DATA_CLK_TIMING);
            //Enable scope controller
            StrobeMemory.GetRegister(STR.SCOPE_ENABLE).Set(1);
            StrobeMemory.WriteSingle(STR.SCOPE_ENABLE);

            //lower global reset
            StrobeMemory.GetRegister(STR.GLOBAL_RESET).InternalValue = 0;
            StrobeMemory.WriteSingle(STR.GLOBAL_RESET);

            StrobeMemory.GetRegister(STR.ENABLE_NEG_DCDC).InternalValue = 1;
            StrobeMemory.WriteSingle(STR.ENABLE_NEG_DCDC);

            //romMemory.ReadSingle(ROM.FPGA_STATUS);
            //if (romMemory.RegisterByName(ROM.FPGA_STATUS).InternalValue != 3)
            //Logger.AddEntry(this, LogMessageType.ECoreError, "!!! DCMs not locked !!!");
            return true;
        }

        override public void Stop()
        {
            hardwareInterface.Stop();
        }

        #endregion

        #region data_handlers

        public byte[] GetBytes()
        {
            int samplesToFetch = 4096;
            int bytesToFetch = samplesToFetch;
            return hardwareInterface.GetData(bytesToFetch);          
        }

        private float[] ConvertByteToVoltage(byte[] buffer, byte yOffset)
        {
            float[] voltage = new float[buffer.Length];

            //this section converts twos complement to a physical voltage value
            float totalOffset = (float)yOffset * calibrationCoefficients[1] + calibrationCoefficients[2];
            for (int i = 0; i < buffer.Length; i++)
            {
                float gainedVal = (float)buffer[i] * calibrationCoefficients[0];
                voltage[i] = gainedVal + totalOffset;
            }
            return voltage;
        }

        public DataPackageScope GetScopeData()
        {
            byte[] buffer = this.GetBytes();
            if(buffer == null) return null;
            //FIXME: Get these scope settings from header
            double samplePeriod = 10e-9; //10ns -> 100MHz fixed for now
            int triggerIndex = 0;


            //Split in 2 channels
            byte[] chA = new byte[buffer.Length / 2];
            byte[] chB = new byte[buffer.Length / 2];
            for (int i = 0; i < chA.Length; i++)
            {
                chB[i] = buffer[2 * i];
                chA[i] = buffer[2*i + 1];
            }

            //construct data package
            DataPackageScope data = new DataPackageScope(samplePeriod, triggerIndex);
            //FIXME: parse package header and set DataPackageScope's trigger index
            //FIXME: Get bytes, split into analog/digital channels and add to scope data
            if (this.disableVoltageConversion)
            {
                data.SetData(ScopeChannel.ChA, Utils.CastArray<byte, float>(chA));
                data.SetData(ScopeChannel.ChB, Utils.CastArray<byte, float>(chB));
            }
            else
            {
                //FIXME: shouldn't the register here be CHA_YOFFSET_VOLTAGE?
                data.SetData(ScopeChannel.ChA, 
                    ConvertByteToVoltage(chA, FpgaSettingsMemory.GetRegister(REG.CHB_YOFFSET_VOLTAGE).Get()));

                //Check if we're in LA mode and fill either analog channel B or digital channels
                if (!this.GetEnableLogicAnalyser())
                {
                    data.SetData(ScopeChannel.ChB,
                        ConvertByteToVoltage(chB, FpgaSettingsMemory.GetRegister(REG.CHB_YOFFSET_VOLTAGE).Get()));
                }
                else
                {
                    //Lot's of shitty code cos I don't know how to write like you do with macros in C...
                    bool[][] digitalSamples = new bool[8][];
                    for(int i = 0; i < 8; i++) digitalSamples[i] = new bool[chB.Length];

                    for (int i = 0; i < chB.Length; i++)
                    {
                        digitalSamples[0][i] = ((chB[i] & (1 << 0)) != 0) ? true : false;
                        digitalSamples[1][i] = ((chB[i] & (1 << 1)) != 0) ? true : false;
                        digitalSamples[2][i] = ((chB[i] & (1 << 2)) != 0) ? true : false;
                        digitalSamples[3][i] = ((chB[i] & (1 << 3)) != 0) ? true : false;
                        digitalSamples[4][i] = ((chB[i] & (1 << 4)) != 0) ? true : false;
                        digitalSamples[5][i] = ((chB[i] & (1 << 5)) != 0) ? true : false;
                        digitalSamples[6][i] = ((chB[i] & (1 << 6)) != 0) ? true : false;
                        digitalSamples[7][i] = ((chB[i] & (1 << 7)) != 0) ? true : false;
                    }
                    data.SetData(ScopeChannel.Digi0, digitalSamples[0]);
                    data.SetData(ScopeChannel.Digi1, digitalSamples[1]);
                    data.SetData(ScopeChannel.Digi2, digitalSamples[2]);
                    data.SetData(ScopeChannel.Digi3, digitalSamples[3]);
                    data.SetData(ScopeChannel.Digi4, digitalSamples[4]);
                    data.SetData(ScopeChannel.Digi5, digitalSamples[5]);
                    data.SetData(ScopeChannel.Digi6, digitalSamples[6]);
                    data.SetData(ScopeChannel.Digi7, digitalSamples[7]);
                }
            }
            return data;
        }

        public bool Connected
        {
            get
            {
                return hardwareInterface.Connected;
            }
        }

        #endregion
    }
}
