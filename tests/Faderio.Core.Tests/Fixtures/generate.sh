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
# The FLAC noise fixtures double as the MP3s' source PCM: the same lavfi expression and seed,
# encoded losslessly. Mp3DecoderOffsetTests and the tagged cells correlate against them, so the
# two must keep coming from one noise() below.
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

rm -f tone-*.flac noise-*.flac tone-*.mp3 noise-*.mp3

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

ls -l *.flac *.mp3
