#!/bin/bash

function to_double() {
        EQVALUE=$(echo "$1" | grep -o -e "=\([0-9]\+\.*[0-9]*\)")
        echo ${EQVALUE:1}
}

EPOCH=$(date '+%s')

RAW=$(vcgencmd get_throttled)
echo -e "vcgencmd.throttled\t$(to_double $RAW)\t${EPOCH}"

RAW=$(vcgencmd measure_temp)
echo -e "vcgencmd.measure_temp\t$(to_double $RAW)\t${EPOCH}"

RAW=$(vcgencmd measure_clock arm)
echo -e "vcgencmd.measure_clock.arm\t$(to_double $RAW)\t${EPOCH}"
RAW=$(vcgencmd measure_clock core)
echo -e "vcgencmd.measure_clock.core\t$(to_double $RAW)\t${EPOCH}"
RAW=$(vcgencmd measure_clock H264)
echo -e "vcgencmd.measure_clock.H264\t$(to_double $RAW)\t${EPOCH}"
RAW=$(vcgencmd measure_clock isp)
echo -e "vcgencmd.measure_clock.isp\t$(to_double $RAW)\t${EPOCH}"
RAW=$(vcgencmd measure_clock v3d)
echo -e "vcgencmd.measure_clock.v3d\t$(to_double $RAW)\t${EPOCH}"
RAW=$(vcgencmd measure_clock uart)
echo -e "vcgencmd.measure_clock.uart\t$(to_double $RAW)\t${EPOCH}"
RAW=$(vcgencmd measure_clock pwm)
echo -e "vcgencmd.measure_clock.pwm\t$(to_double $RAW)\t${EPOCH}"
RAW=$(vcgencmd measure_clock emmc)
echo -e "vcgencmd.measure_clock.emmc\t$(to_double $RAW)\t${EPOCH}"
RAW=$(vcgencmd measure_clock pixel)
echo -e "vcgencmd.measure_clock.pixel\t$(to_double $RAW)\t${EPOCH}"
RAW=$(vcgencmd measure_clock vec)
echo -e "vcgencmd.measure_clock.vec\t$(to_double $RAW)\t${EPOCH}"
RAW=$(vcgencmd measure_clock hdmi)
echo -e "vcgencmd.measure_clock.hdmi\t$(to_double $RAW)\t${EPOCH}"
RAW=$(vcgencmd measure_clock dpi)
echo -e "vcgencmd.measure_clock.dpi\t$(to_double $RAW)\t${EPOCH}"

RAW=$(vcgencmd measure_volts core)
echo -e "vcgencmd.measure_volts.core\t$(to_double $RAW)\t${EPOCH}"
RAW=$(vcgencmd measure_volts sdram_c)
echo -e "vcgencmd.measure_volts.sdram_c\t$(to_double $RAW)\t${EPOCH}"
RAW=$(vcgencmd measure_volts sdram_i)
echo -e "vcgencmd.measure_volts.sdram_i\t$(to_double $RAW)\t${EPOCH}"
RAW=$(vcgencmd measure_volts sdram_p)
echo -e "vcgencmd.measure_volts.sdram_p\t$(to_double $RAW)\t${EPOCH}"
