#!/usr/bin/env bash
# Regenerates the checked-in compressed fixtures. Run from this directory with ffmpeg on PATH.
#
# Fixtures are checked in rather than generated at test time, by decision (ticket 06, ADR 0002):
# a cell that skips because ffmpeg is absent is green while asserting nothing. This script is
# the provenance. Every fixture is 2 s. The grid is the promised table in
# docs/supported-audio.md (ticket 08), one tone and one noise fixture per cell (ticket 14):
#
#   tone-<rate>-<bits>-<ch>.flac            0.5 x 1 kHz sine   purity, completeness, header length
#   noise-<rate>-<bits>-<ch>.flac           pink noise         seek landing by cross-correlation
#   tone-<variant>-<rate>-<ch>.mp3          as above, MP3
#   noise-<variant>-<rate>-<ch>.mp3
#   tone-<encoder>-<rate>-<ch>.wma          as above, WMA: research, not a promised row
#   noise-<encoder>-<rate>-<ch>.wma
#
# FLAC: 16/24-bit x 44.1/48/96 kHz x mono/stereo, twelve cells. "-sample_fmt s32" makes
# ffmpeg's FLAC encoder write 24-bit.
#
# MP3: three variants x 44.1/48 kHz x mono/stereo, twelve cells. Each variant is a way the
# add-time probe learns the length (ADR 0010):
#   cbrinfo   320 kbps CBR with LAME's Info tag (ffmpeg's default), like 447 of the library's
#             654 MP3s (ticket 08 survey): the probe reads the frame count from the tag.
#   cbrbare   320 kbps CBR with no Xing/Info tag ("-write_xing 0"): the probe divides the audio
#             bytes by the frame size.
#   vbr       LAME V2 ("-q:a 2"), Xing tag: the frame count from the tag.
# The Info and Xing tags carry ffmpeg's LAME extension (encoder "Lavc", delay 576), which the
# probe trims by (issue 18): their length is the source's 88200 or 96000 samples, not whole
# frames. Two more kinds of tag are crafted from these at test time by CraftedMp3, because
# lame.exe is not available here: a "LAME" encoder string, and tags that fail each check.
#
# WMA (research into MusicBee's BASS, MusicBeeBassWmaTests; not in docs/supported-audio.md):
# WMA 9 Standard x 44.1/48 kHz x mono/stereo, 128 kbps stereo, from two encoders, because real
# WMA files come from Windows Media Player and ffmpeg's muxer is not it:
#   ffmpeg    ffmpeg's wmav2 encoder; 64 kbps mono.
#   windows   Windows' own encoder (the Windows Media Format runtime), reached through MusicBee's
#             basswma.dll by encode-wma.ps1, which needs MusicBee installed and runs in 32-bit
#             PowerShell. It is fed the same lavfi PCM as raw float. It offers mono only up to
#             48 kbps at 44.1 kHz and not at all at 48 kHz, so it has three cells, not four.
#
# The FLAC noise fixtures double as the MP3s' source PCM: the same lavfi expression and seed,
# encoded losslessly. Mp3DecoderOffsetTests and the tagged cells correlate against them, so the
# two must keep coming from one noise() below.
#
# Two FLACs outside the grid, for the frame table the decoder package's pre-scan builds (build
# issue 22). Each is shaped like files in the author's music folder:
#   silent-tail-44100-16-2.flac   1.5 s of pink noise padded with digital silence to 20 whole
#                 4608-sample blocks (92160 samples, 2.09 s). The silent frames are 14 bytes
#                 (constant subframes), and the package never finds the last one: it sits in
#                 the file's last 16 bytes. 110 of 245 FLACs in the music folder end this way.
#   false-header-44100-16-1.flac  2 s of full-scale white noise, which the encoder stores
#                 verbatim, so bytes written into the PCM appear in the file unchanged. Six
#                 bytes at sample 70120 (in frame 15 of 20) are a valid frame header of the
#                 same format: FF F8 89 08 09 2C, fixed-blocksize frame 9 of 256 samples, 44.1
#                 kHz, mono, 16-bit, and its CRC-8. The package's pre-scan takes it for a frame,
#                 as it did in 5 of those 245 files. The bytes are written swapped in pairs:
#                 the raw PCM is s16le, a verbatim subframe is big-endian.
#
# A sine, not a full-scale one: lavfi's "sine" source is full scale, which a lossy encoder
# overshoots into clipping. The pink noise is what the seek landing correlates against; a tone
# is periodic and would match every cycle.
#
# Levels: lavfi sources are mono and "-ac 2" upmixes through swresample, which applies -3 dB
# (0.707). So the stereo fixtures are quieter on disk than the mono ones. Harmless: the tests
# self-calibrate against a sequential decode of the same file. Do not "fix" the decoders for
# it. (Verified from the raw s16 bytes, not through a decoder.)
set -euo pipefail
cd "$(dirname "$0")"

tone()  { echo "aevalsrc='0.5*sin(2*PI*1000*t)':s=$1:d=2"; }
noise() { echo "anoisesrc=color=pink:sample_rate=$1:duration=2:seed=42:amplitude=0.5"; }

rm -f tone-*.flac noise-*.flac tone-*.mp3 noise-*.mp3 tone-*.wma noise-*.wma silent-tail-*.flac false-header-*.flac

for rate in 44100 48000 96000; do
  for bits in 16 24; do
    fmt=s16; [ "$bits" = 24 ] && fmt=s32
    for ch in 1 2; do
      ffmpeg -y -loglevel error -f lavfi -i "$(tone $rate)"  -ac $ch -sample_fmt $fmt "tone-$rate-$bits-$ch.flac"
      ffmpeg -y -loglevel error -f lavfi -i "$(noise $rate)" -ac $ch -sample_fmt $fmt "noise-$rate-$bits-$ch.flac"
    done
  done
done

for rate in 44100 48000; do
  for ch in 1 2; do
    ffmpeg -y -loglevel error -f lavfi -i "$(tone $rate)"  -ac $ch -c:a libmp3lame -b:a 320k "tone-cbrinfo-$rate-$ch.mp3"
    ffmpeg -y -loglevel error -f lavfi -i "$(noise $rate)" -ac $ch -c:a libmp3lame -b:a 320k "noise-cbrinfo-$rate-$ch.mp3"
    ffmpeg -y -loglevel error -f lavfi -i "$(tone $rate)"  -ac $ch -c:a libmp3lame -b:a 320k -write_xing 0 "tone-cbrbare-$rate-$ch.mp3"
    ffmpeg -y -loglevel error -f lavfi -i "$(noise $rate)" -ac $ch -c:a libmp3lame -b:a 320k -write_xing 0 "noise-cbrbare-$rate-$ch.mp3"
    ffmpeg -y -loglevel error -f lavfi -i "$(tone $rate)"  -ac $ch -c:a libmp3lame -q:a 2 "tone-vbr-$rate-$ch.mp3"
    ffmpeg -y -loglevel error -f lavfi -i "$(noise $rate)" -ac $ch -c:a libmp3lame -q:a 2 "noise-vbr-$rate-$ch.mp3"
  done
done

ps32=/c/Windows/SysWOW64/WindowsPowerShell/v1.0/powershell.exe
f32=$(mktemp)
for rate in 44100 48000; do
  for ch in 1 2; do
    kbps=128; [ "$ch" = 1 ] && kbps=64
    winbps=128000; [ "$ch" = 1 ] && winbps=48000
    for signal in tone noise; do
      src=$($signal $rate)
      ffmpeg -y -loglevel error -f lavfi -i "$src" -ac $ch -c:a wmav2 -b:a ${kbps}k "$signal-ffmpeg-$rate-$ch.wma"
      [ "$rate-$ch" = 48000-1 ] && continue
      ffmpeg -y -loglevel error -f lavfi -i "$src" -ac $ch -f f32le "$f32"
      "$ps32" -NoProfile -ExecutionPolicy Bypass -File encode-wma.ps1 "$(cygpath -w "$f32")"         "$signal-windows-$rate-$ch.wma" $rate $ch $winbps
    done
  done
done
rm -f "$f32"

ffmpeg -y -loglevel error -f lavfi -i "anoisesrc=color=pink:sample_rate=44100:duration=1.5:seed=42:amplitude=0.5" \
  -af apad=whole_len=92160 -ac 2 -sample_fmt s16 silent-tail-44100-16-2.flac

raw=$(mktemp)
ffmpeg -y -loglevel error -f lavfi -i "anoisesrc=color=white:sample_rate=44100:duration=2:seed=42:amplitude=1" \
  -ac 1 -f s16le "$raw"
printf '\xF8\xFF\x08\x89\x2C\x09' | dd of="$raw" bs=1 seek=$((70120 * 2)) conv=notrunc status=none
ffmpeg -y -loglevel error -f s16le -ar 44100 -ac 1 -i "$raw" false-header-44100-16-1.flac
rm -f "$raw"

ls -l *.flac *.mp3 *.wma
