#!/bin/bash

# Ref. https://www.zumid.net/entry/raspberry-pi-bme280/
# を元に1回の実行で温湿度気圧を出力するようにしたもの。

bme280py_path="/usr/local/bin/bme280/bme280.py"

SECONDS=`date '+%s'`

VALUE=`python3 ${bme280py_path}`
echo $VALUE
TEMP=`echo "$VALUE" | awk '{if ($1 == "temperature"){print $3}}'`
HUMI=`echo "$VALUE" | awk '{if ($1 == "humidity"){print $3}}'`
PRES=`echo "$VALUE" | awk '{if ($1 == "pressure"){print $3}}'`

echo -e "bme280.temperature\t${TEMP}\t${SECONDS}"
echo -e "bme280.humidity\t${HUMI}\t${SECONDS}"
echo -e "bme280.pressure\t${PRES}\t${SECONDS}"
