﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ECore.DeviceMemories;
using Common;
using ECore.HardwareInterfaces;

namespace ECore.Devices
{
    partial class SmartScope
    {

#if DEBUG
        public 
#endif
        static readonly double[] validDividers = { 1, 6, 36 };
#if DEBUG
        public 
#endif
        static readonly double[] validMultipliers = { 1.1, 2, 3 };

        private class Range
        {
            public Range(float minimum, float maximum)
            {
                this.minimum = minimum;
                this.maximum = maximum;
            }
            public float minimum;
            public float maximum;
        }

        private Dictionary<AnalogChannel, Coupling> coupling;
        private Dictionary<AnalogChannel, ProbeDivision> probeSettings;
        private Dictionary<AnalogChannel, Range> verticalRanges;
        private Dictionary<AnalogChannel, float> yOffset;

        private double holdoff;
#if DEBUG
        public 
#endif
        static byte yOffsetMax = 200;
#if DEBUG
        public 
#endif
        static byte yOffsetMin = 10;

        float triggerThreshold = 0f;

        public bool ChunkyAcquisitions { get; private set; }

        #region helpers

        private void validateDivider(double div)
        {
            if (!validDividers.Contains(div))
                throw new ValidationException(
                    "Invalid divider, valid values are: " +
                    String.Join(", ", validDividers.Select(x => x.ToString()).ToArray())
                    );
        }
        private void validateMultiplier(double mul)
        {
            if(!validMultipliers.Contains(mul))
                throw new ValidationException(
                    "Invalid multiplier, valid values are: " + 
                    String.Join(", ", validMultipliers.Select(x => x.ToString()).ToArray())
                    );
        }
        private void toggleUpdateStrobe()
        {
            if (!Connected) return;
            StrobeMemory[STR.SCOPE_UPDATE].WriteImmediate(false);
            StrobeMemory[STR.SCOPE_UPDATE].WriteImmediate(true);
        }
        private float ProbeScaleHostToScope(AnalogChannel ch, float volt)
        {
            return volt / ProbeScaleFactors[probeSettings[ch]];
        }
        private float ProbeScaleScopeToHost(AnalogChannel ch, float volt)
        {
            return volt * ProbeScaleFactors[probeSettings[ch]];
        }
        public void CommitSettings()
        {
            try
            {
                int registersWritten = 0;
                foreach (DeviceMemory mem in memories)
                {
                    registersWritten += mem.Commit();
                }
                if (registersWritten > 0)
                    toggleUpdateStrobe();
            }
            catch (ScopeIOException e)
            {
                Logger.Error("I/O failure while committing scope settings (" + e.Message + ")");
            }
        }
        #endregion

        #region vertical

        /// <summary>
        /// Sets vertical offset of a channel
        /// </summary>
        /// <param name="channel">0 or 1 (channel A or B)</param>
        /// <param name="offset">Vertical offset in Volt</param>
        public void SetYOffset(AnalogChannel channel, float offset)
        {
            yOffset[channel] = offset;
            if (!Connected) return;
            //FIXME: convert offset to byte value
            REG r = (channel == AnalogChannel.ChA) ? REG.CHA_YOFFSET_VOLTAGE : REG.CHB_YOFFSET_VOLTAGE;
            Logger.Debug("Set DC coupling for channel " + channel + " to " + offset + "V");
            //Offset: 0V --> 150 - swing +-0.9V
            
            //Let ADC output of 127 be the zero point of the Yoffset
            double[] c = channelSettings[channel].coefficients;
            byte offsetByte = (byte)Math.Min(yOffsetMax, Math.Max(yOffsetMin, -(ProbeScaleHostToScope(channel, offset) + c[2] + c[0] * 127) / c[1] ));
            FpgaSettingsMemory[r].Set(offsetByte);
            Logger.Debug(String.Format("Yoffset Ch {0} set to {1} V = byteval {2}", channel, offset, offsetByte));
        }

        private float ConvertYOffsetByteToVoltage(AnalogChannel channel, byte value)
        {
            double[] c = channelSettings[channel].coefficients;
            float voltageSet = (float)(-value * c[1] - c[2] - c[0] * 127.0);
            return ProbeScaleScopeToHost(channel, voltageSet);
        }

		public float GetYOffset(AnalogChannel channel)
		{
            REG r = (channel == AnalogChannel.ChA) ? REG.CHA_YOFFSET_VOLTAGE : REG.CHB_YOFFSET_VOLTAGE;
            byte offsetByte = FpgaSettingsMemory[r].GetByte();
            return ConvertYOffsetByteToVoltage(channel, offsetByte);
        }

        /// <summary>
        /// Sets and uploads the divider and multiplier what are optimal for the requested range
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="minimum"></param>
        /// <param name="maximum"></param>
        public void SetVerticalRange(AnalogChannel channel, float minimum, float maximum)
        {
            if (!Connected) return;
            //The voltage range for div/mul = 1/1
            //20140808: these seem to be OK: on div0/mult0 the ADC input range is approx 1.3V
            float baseMin = -0.6345f; //V
            float baseMax = 0.6769f; //V

            //Walk through dividers/multipliers till requested range fits
            //this walk assumes it starts with the smallest range, and that range is only increasing
            int dividerIndex = 0;
            int multIndex = 0;

            verticalRanges[channel] = new Range(minimum, maximum);

            for (int i = 0; i < rom.computedDividers.Length * rom.computedMultipliers.Length; i++)
            {
                dividerIndex= i / rom.computedMultipliers.Length;
                multIndex = rom.computedMultipliers.Length - (i % rom.computedMultipliers.Length) - 1;
                if (
                    (ProbeScaleHostToScope(channel, maximum) < baseMax * rom.computedDividers[dividerIndex] / rom.computedMultipliers[multIndex])
                    &&
                    (ProbeScaleHostToScope(channel, minimum) > baseMin * rom.computedDividers[dividerIndex] / rom.computedMultipliers[multIndex])
                    )
                    break;
            }
            SetDivider(channel, validDividers[dividerIndex]);
            SetMultiplier(channel, validMultipliers[multIndex]);
            channelSettings[channel] = rom.getCalibration(channel, validDividers[dividerIndex], validMultipliers[multIndex]);
            SetYOffset(channel, yOffset[channel]);
            yOffset[channel] = GetYOffset(channel);
            if (channel == triggerAnalog.channel)
            {
                SetTriggerAnalog(this.triggerAnalog);
                //SetTriggerThreshold(this.triggerThreshold);
            }
        }

        public void SetProbeDivision(AnalogChannel ch, ProbeDivision division)
        {
            probeSettings[ch] = division;
            SetVerticalRange(ch, verticalRanges[ch].minimum, verticalRanges[ch].maximum);
        }

        public ProbeDivision GetProbeDivision(AnalogChannel ch)
        {
            return probeSettings[ch];
        }

		/// <summary>
		/// Set divider of a channel
		/// </summary>
		/// <param name="channel">0 or 1 (channel A or B)</param>
		/// <param name="divider">1, 10 or 100</param>
		#if DEBUG
		public
		#else
		private
		#endif
		void SetDivider(AnalogChannel channel, double divider)
		{
			validateDivider(divider);
            byte div = (byte)(Array.IndexOf(validDividers, divider));
			int bitOffset = channel.Value * 4;
			byte mask = (byte)(0x3 << bitOffset);

			byte divMul = FpgaSettingsMemory[REG.DIVIDER_MULTIPLIER].GetByte();
			divMul = (byte)((divMul & ~mask) + (div << bitOffset));
			FpgaSettingsMemory[REG.DIVIDER_MULTIPLIER].Set(divMul);
		}

		///<summary>
		///Set multiplier of a channel
		///</summary>
		///<param name="channel">0 or 1 (channel A or B)</param>
		///<param name="multiplier">Set input stage multiplier (?? or ??)</param>
		#if DEBUG
		public
		#else
		private
		#endif
		void SetMultiplier(AnalogChannel channel, double multiplier)
		{
			validateMultiplier(multiplier);

			int bitOffset = channel.Value * 4;
			byte mul = (byte)(Array.IndexOf(validMultipliers, multiplier) << 2);
			byte mask = (byte)(0xC << bitOffset);

			byte divMul = FpgaSettingsMemory[REG.DIVIDER_MULTIPLIER].GetByte();
			divMul = (byte)((divMul & ~mask) + (mul << bitOffset));
			FpgaSettingsMemory[REG.DIVIDER_MULTIPLIER].Set(divMul);
		}

#if DEBUG
        public void SetYOffsetByte(AnalogChannel channel, byte offset)
        {
            REG r = channel == AnalogChannel.ChA ? REG.CHA_YOFFSET_VOLTAGE : REG.CHB_YOFFSET_VOLTAGE;
            Logger.Debug("Set Y offset for channel " + channel + " to " + offset + " (int value)");
            FpgaSettingsMemory[r].Set(offset);
        }
#endif

        private bool disableVoltageConversion = false;
        /// <summary>
        /// Disable the voltage conversion to have GetVoltages return the raw bytes as sample values (cast to float though)
        /// </summary>
        /// <param name="disable"></param>
        public void SetDisableVoltageConversion(bool disable)
        {
            this.disableVoltageConversion = disable;
        }

        public void SetCoupling(AnalogChannel channel, Coupling coupling)
        {
            STR dc = channel == AnalogChannel.ChA ? STR.CHA_DCCOUPLING : STR.CHB_DCCOUPLING;
            bool enableDc = coupling == Coupling.DC;
            Logger.Debug("Set DC coupling for channel " + channel + (enableDc ? " ON" : " OFF"));
            StrobeMemory[dc].Set(enableDc);
        }
        public Coupling GetCoupling(AnalogChannel channel)
        {
            STR dc = channel == AnalogChannel.ChA ? STR.CHA_DCCOUPLING : STR.CHB_DCCOUPLING;
            return StrobeMemory[dc].GetBool() ? Coupling.DC : Coupling.AC;
        }

        #endregion

        #region horizontal
        ///<summary>
        ///Set scope trigger level
        ///</summary>
        ///<param name="trigger">Trigger condition</param>
        public void SetTriggerAnalog(AnalogTriggerValue trigger)
        {
            if (!Connected) return;
            this.triggerAnalog = trigger;

            /* Set Level */
            double[] coefficients = channelSettings[trigger.channel].coefficients;
            REG offsetRegister = trigger.channel == AnalogChannel.ChB ? REG.CHB_YOFFSET_VOLTAGE : REG.CHA_YOFFSET_VOLTAGE;
            double level = 0;
            if(coefficients != null)
                level = (ProbeScaleHostToScope(trigger.channel, trigger.level) - FpgaSettingsMemory[offsetRegister].GetByte() * coefficients[1] - coefficients[2]) / coefficients[0];
            if (level < 0) level = 0;
            if (level > 255) level = 255;

            Logger.Debug(" Set trigger level to " + trigger.level + "V (" + level + ")");
            FpgaSettingsMemory[REG.TRIGGER_LEVEL].Set((byte)level);

            /* Set Channel */
            Logger.Debug(" Set trigger channel to " + (triggerAnalog.channel == AnalogChannel.ChA ? " CH A" : "CH B"));
            FpgaSettingsMemory[REG.TRIGGER_MODE].Set(
                (byte)(
                    (FpgaSettingsMemory[REG.TRIGGER_MODE].GetByte() & 0xF3) +
                    (triggerAnalog.channel.Value << 2)
                        ));
            
            /* Set Direction */
            FpgaSettingsMemory[REG.TRIGGER_MODE].Set(
                (byte)(
                    (FpgaSettingsMemory[REG.TRIGGER_MODE].GetByte() & 0xCF) +
                    (((int)triggerAnalog.direction << 4) & 0x30)
                    )
            );
            Logger.Debug(" Set trigger channel to " + Enum.GetName(typeof(TriggerDirection), triggerAnalog.direction));

        }
        public void SetForceTrigger()
        {
            if(Ready)
                StrobeMemory[STR.FORCE_TRIGGER].WriteImmediate(true);
        }
#if DEBUG
        public void SetTriggerByte(byte level)
        {
            FpgaSettingsMemory[REG.TRIGGER_LEVEL].Set(level);
        }
#endif
        /// <summary>
        /// Choose channel upon which to trigger
        /// </summary>
        /// <param name="channel"></param>
        public void SetTriggerChannel(AnalogChannel channel)
        {
            this.triggerAnalog.channel = channel;
            SetTriggerAnalog(this.triggerAnalog);
        }


        public AnalogChannel GetTriggerChannel()
        {         
            int chNumber = (FpgaSettingsMemory[REG.TRIGGER_MODE].GetByte() & 0x0C) >> 2;
            return AnalogChannel.List.Single(x => x.Value == chNumber);
        }

        /// <summary>
        /// Choose between rising or falling trigger
        /// </summary>
        /// <param name="direction"></param>
        private void SetTriggerDirection(TriggerDirection direction)
        {
            triggerAnalog.direction = direction;
            SetTriggerAnalog(this.triggerAnalog);
        }
        public void SetTriggerWidth(uint width)
        {
            FpgaSettingsMemory[REG.TRIGGER_WIDTH].Set((byte)width);
        }
        public uint GetTriggerWidth()
        {
            return (uint)FpgaSettingsMemory[REG.TRIGGER_WIDTH].GetByte();
        }

        public void SetTriggerThreshold(float threshold)
        {
            Logger.Warn("Trigger threshold is not implemented!");
            return;
            //throw new NotImplementedException("Forget it");
            triggerThreshold = threshold;
            double level = 0;
            double[] coefficients = channelSettings[GetTriggerChannel()].coefficients;
            if (coefficients != null)
                level = (ProbeScaleHostToScope(triggerAnalog.channel, triggerThreshold) - coefficients[2]) / coefficients[0];
            if (level < 0) level = 0;
            if (level > 255) level = 255;
            FpgaSettingsMemory[REG.TRIGGER_THRESHOLD].Set((byte)level);
        }
        public float GetTriggerThreshold()
        {
            return triggerThreshold;
        }

        public void SetAcquisitionMode(AcquisitionMode mode)
        {
            FpgaSettingsMemory[REG.TRIGGER_MODE].Set(
                (byte)(
                    (FpgaSettingsMemory[REG.TRIGGER_MODE].GetByte() & 0x3F) +
                    (((int)mode << 6) & 0xC0)
                )
            );
        }
        public void SetAcquisitionRunning(bool running)
        {
            if (!Connected) return;
            STR s;
            if (running)
            {
                s = STR.ACQ_START;
                acquiring = true;
                stopPending = false;
            }
            else
            {
                //Don't assume we'll stop immediately
                s = STR.ACQ_STOP;
            }
            
            StrobeMemory[s].WriteImmediate(true);
        }

        public bool CanRoll
        {
            get
            {
                return FpgaSettingsMemory[REG.INPUT_DECIMATION].GetByte() >= INPUT_DECIMATION_MIN_FOR_ROLLING_MODE;
            }
        }
        public bool Rolling
        {
            get
            {
                return CanRoll && StrobeMemory[STR.ROLL].GetBool();
            }
        }
        public void SetRolling(bool enable)
        {
            StrobeMemory[STR.ROLL].Set(enable);
        }

        public bool Running { get { return Ready && acquiring; } }
        public bool StopPending { get { return Ready && stopPending; } }

        public void SetTriggerDigital(Dictionary<DigitalChannel, DigitalTriggerValue> condition)
        {
            int rising  = condition.Aggregate(0, (r, x) => r + ((x.Value == DigitalTriggerValue.R  ? 1 : 0) << x.Key.Value));
            int falling = condition.Aggregate(0, (r, x) => r + ((x.Value == DigitalTriggerValue.F ? 1 : 0) << x.Key.Value));
            int high    = condition.Aggregate(0, (r, x) => r + ((x.Value == DigitalTriggerValue.H    ? 1 : 0) << x.Key.Value));
            int low     = condition.Aggregate(0, (r, x) => r + ((x.Value == DigitalTriggerValue.L     ? 1 : 0) << x.Key.Value));
            FpgaSettingsMemory[REG.DIGITAL_TRIGGER_RISING].Set((byte)rising);
            FpgaSettingsMemory[REG.DIGITAL_TRIGGER_FALLING].Set((byte)falling);
            FpgaSettingsMemory[REG.DIGITAL_TRIGGER_HIGH].Set((byte)high);
            FpgaSettingsMemory[REG.DIGITAL_TRIGGER_LOW].Set((byte)low);

            //FIXME: We currently don't support passing the LA data through channel B, so the trigger needs to be set to chA
            SetTriggerAnalog(new AnalogTriggerValue() { channel = AnalogChannel.ChA, direction = TriggerDirection.RISING, level = 0} );
        }

        public void SetTimeRange(double timeRange)
        {
            double defaultTimeRange = GetDefaultTimeRange();
            double timeScaler = timeRange / defaultTimeRange;
            byte inputDecimation;
            if (timeScaler > 1)
                inputDecimation = (byte)Math.Ceiling(Math.Log(timeScaler, 2));
            else
                inputDecimation = 0;

            if (inputDecimation > INPUT_DECIMATION_MAX)
                inputDecimation = INPUT_DECIMATION_MAX;

            FpgaSettingsMemory[REG.INPUT_DECIMATION].Set(inputDecimation);
            ChunkyAcquisitions = inputDecimation >= INPUT_DECIMATION_MIN_FOR_ROLLING_MODE;
            SetTriggerHoldOff(holdoff);
        }

        public double ConvertSamplesToTime(int samples)
        {
            return samples * BASE_SAMPLE_PERIOD * Math.Pow(2, FpgaSettingsMemory[REG.INPUT_DECIMATION].GetByte());
        }

        public int SubSampleRate {
			get { return FpgaSettingsMemory[REG.INPUT_DECIMATION].GetByte(); }
		}

        public double GetTimeRange()
        {
            return GetDefaultTimeRange() * Math.Pow(2, FpgaSettingsMemory[REG.INPUT_DECIMATION].GetByte());
        }
        ///<summary>
        ///Scope hold off
        ///</summary>
        ///<param name="time">Store [time] before trigger</param>
        public void SetTriggerHoldOff(double time)
        {
            this.holdoff = time;
            byte inputDecimation = FpgaSettingsMemory[REG.INPUT_DECIMATION].GetByte();
            Int32 samples = (Int32)(time / (BASE_SAMPLE_PERIOD * Math.Pow(2,  inputDecimation)));
            Logger.Debug(" Set trigger holdoff to " + time * 1e6 + "us or " + samples + " samples " );
            FpgaSettingsMemory[REG.TRIGGERHOLDOFF_B0].Set((byte)(samples)); 
            FpgaSettingsMemory[REG.TRIGGERHOLDOFF_B1].Set((byte)(samples >> 8));
            FpgaSettingsMemory[REG.TRIGGERHOLDOFF_B2].Set((byte)(samples >> 16));
            FpgaSettingsMemory[REG.TRIGGERHOLDOFF_B3].Set((byte)(samples >> 24));
        }

        #endregion

        #region other        
        /// <summary>
        /// Returns the timerange when decimation is 1
        /// </summary>
        /// <returns></returns>
        public double GetDefaultTimeRange() {
            return BASE_SAMPLE_PERIOD * (NUMBER_OF_SAMPLES); 
        }

        /// <summary>
        /// Print me as HEX
        /// </summary>
        /// <returns></returns>
        public uint GetFpgaFirmwareVersion()
        {
            return (UInt32)(FpgaRom[ROM.FW_GIT0].Read().GetByte() +
                   (UInt32)(FpgaRom[ROM.FW_GIT1].Read().GetByte() << 8) +
                   (UInt32)(FpgaRom[ROM.FW_GIT2].Read().GetByte() << 16) +
                   (UInt32)(FpgaRom[ROM.FW_GIT3].Read().GetByte() << 24));
        }

        public byte[] GetPicFirmwareVersion() {
            hardwareInterface.SendCommand(SmartScopeUsbInterfaceHelpers.PIC_COMMANDS.PIC_VERSION);
            byte[] response = hardwareInterface.ReadControlBytes(16);
            return response.Skip(4).Take(3).Reverse().ToArray();
        }

        #endregion

        #region Logic Analyser

        public void SetEnableLogicAnalyser(bool enable)
        {
            if (!Ready) return;
            StrobeMemory[STR.LA_ENABLE].Set(enable);
            if(enable)
                StrobeMemory[STR.AWG_ENABLE].Set(false);
        }
        public void SetLogicAnalyserChannel(AnalogChannel channel)
        {
            StrobeMemory[STR.LA_CHANNEL].Set(channel == AnalogChannel.ChB);
        }

        #endregion
    }
}
