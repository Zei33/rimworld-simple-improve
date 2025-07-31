#!/bin/bash

export FrameworkPathOverride=/opt/homebrew/opt/mono/lib/mono/4.7.2-api
dotnet build rimworld-simple-improve.sln -c Release

ln -sfn ~/Programming/Rimworld/rimworld-simple-improve "${RimWorldDir}/Mods/SimpleImprove"