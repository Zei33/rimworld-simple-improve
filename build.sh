#!/bin/bash
set -e

export FrameworkPathOverride=/opt/homebrew/opt/mono/lib/mono/4.7.2-api

if [ -z "${RimWorldDir}" ]; then
  echo "RimWorldDir is not set. Export it to your RimWorld install before building." >&2
  exit 1
fi

rm -Rf release
dotnet build rimworld-simple-improve.sln -c Release
mkdir -p release

cp -r About release/About

# Stage 1.6 by naming what ships, rather than copying it wholesale and deleting
# afterwards. `cp -r 1.6 release` put 23 C# source files, the vendored Harmony
# reference and two developer READMEs into every subscriber's mod folder, which is
# about an eighth of the download and none of it is read by the game. An allow-list
# is also the shape that stays correct when a folder is added: a new source folder
# is simply not staged, where a deny-list would ship it until somebody noticed.
mkdir -p release/1.6/Assemblies/net472

# Only our own assembly. Anything else here is a build artefact or a game assembly
# RimWorld already has loaded, and ModAssemblyHandler.ReloadAll loads every .dll at
# any depth, filtered on extension alone, so shipping one is not inert. The vendored
# 0Harmony.dll under Libraries/ is reference-only and is never staged.
cp 1.6/Assemblies/net472/SimpleImprove.dll release/1.6/Assemblies/net472/

cp -r 1.6/Defs release/1.6/Defs
cp -r 1.6/Languages release/1.6/Languages
cp -r 1.6/Textures release/1.6/Textures

mkdir -p release/1.6/Patches
cp 1.6/Patches/*.xml release/1.6/Patches/

# 1.6/Patches holds both the XML PatchOperations the game reads and the C# Harmony
# patches it does not, and 1.6/Languages and 1.6/Textures each carry a developer
# README that has been shipping to subscribers. Keep only what the game loads.
find release/1.6/Languages -type f ! -name '*.xml' -delete
find release/1.6/Textures -type f ! -name '*.png' -delete

rm -Rf "${RimWorldDir}/Mods/SimpleImprove"
cp -r release "${RimWorldDir}/Mods/SimpleImprove"
rm -Rf release
