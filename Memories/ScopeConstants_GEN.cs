﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LabNation.DeviceInterface.Memories
{
#if DEBUG
	public
#else
	internal
#endif
	 enum REG
    {
		STROBE_UPDATE = 0,
		SPI_ADDRESS = 1,
		SPI_WRITE_VALUE = 2,
		DIVIDER_MULTIPLIER = 3,
		CHA_YOFFSET_VOLTAGE = 4,
		CHB_YOFFSET_VOLTAGE = 5,
		TRIGGER_PWM = 6,
		TRIGGER_LEVEL = 7,
		TRIGGER_MODE = 8,
		TRIGGER_PW_MIN_B0 = 9,
		TRIGGER_PW_MIN_B1 = 10,
		TRIGGER_PW_MIN_B2 = 11,
		TRIGGER_PW_MAX_B0 = 12,
		TRIGGER_PW_MAX_B1 = 13,
		TRIGGER_PW_MAX_B2 = 14,
		INPUT_DECIMATION = 15,
		ACQUISITION_DEPTH = 16,
		TRIGGERHOLDOFF_B0 = 17,
		TRIGGERHOLDOFF_B1 = 18,
		TRIGGERHOLDOFF_B2 = 19,
		TRIGGERHOLDOFF_B3 = 20,
		VIEW_DECIMATION = 21,
		VIEW_OFFSET_B0 = 22,
		VIEW_OFFSET_B1 = 23,
		VIEW_OFFSET_B2 = 24,
		VIEW_ACQUISITIONS = 25,
		VIEW_BURSTS = 26,
		VIEW_EXCESS_B0 = 27,
		VIEW_EXCESS_B1 = 28,
		DIGITAL_TRIGGER_RISING = 29,
		DIGITAL_TRIGGER_FALLING = 30,
		DIGITAL_TRIGGER_HIGH = 31,
		DIGITAL_TRIGGER_LOW = 32,
		DIGITAL_OUT = 33,
		GENERATOR_DECIMATION_B0 = 34,
		GENERATOR_DECIMATION_B1 = 35,
		GENERATOR_DECIMATION_B2 = 36,
		GENERATOR_DECIMATION_B3 = 37,
		GENERATOR_SAMPLES_B0 = 38,
		GENERATOR_SAMPLES_B1 = 39,
    }

#if DEBUG
	public
#else
	internal
#endif
	 enum STR
    {
		GLOBAL_RESET = 0,
		INIT_SPI_TRANSFER = 1,
		GENERATOR_TO_AWG = 2,
		LA_ENABLE = 3,
		SCOPE_ENABLE = 4,
		SCOPE_UPDATE = 5,
		FORCE_TRIGGER = 6,
		VIEW_UPDATE = 7,
		VIEW_SEND_OVERVIEW = 8,
		VIEW_SEND_PARTIAL = 9,
		ACQ_START = 10,
		ACQ_STOP = 11,
		CHA_DCCOUPLING = 12,
		CHB_DCCOUPLING = 13,
		ENABLE_ADC = 14,
		OVERFLOW_DETECT = 15,
		ENABLE_NEG = 16,
		ENABLE_RAM = 17,
		DOUT_3V_5V = 18,
		EN_OPAMP_B = 19,
		GENERATOR_TO_DIGITAL = 20,
		ROLL = 21,
		LA_CHANNEL = 22,
    }

#if DEBUG
	public
#else
	internal
#endif
	 enum ROM
    {
		FW_MSB = 0,
		FW_LSB = 1,
		FW_GIT0 = 2,
		FW_GIT1 = 3,
		FW_GIT2 = 4,
		FW_GIT3 = 5,
		SPI_RECEIVED_VALUE = 6,
		STROBES = 7,
    }

}
