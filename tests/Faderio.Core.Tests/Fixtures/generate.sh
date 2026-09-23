#!/usr/bin/env bash
# Regenerates the checked-in compressed fixtures. Run from this directory with ffmpeg on PATH.
#
# Fixtures are checked in rather than generated at test time, by decision (ticket 06, ADR 0002):
# a cell that skips because ffmpeg is absent is green while asserting nothing. This script is
# the provenance. Every fixture is 2 s and stereo, and all but the two ticket-15 ones are
# 44.1 kHz, so no resampling or channel mapping happens on the way in and the decoder is the
# only thing under test.
#
#   tone-1k.flac   1 kHz sine            purity and completeness
#   ramp.flac      0 -> 0.9 linear ramp  seek landing: the sample value IS the position
#   tone-1k.mp3    1 kHz sine, MP3 CBR   the add-time probe: first frame header + Info tag
#   tone-1k-24bit.flac   1 kHz sine, 44.1 kHz / 24-bit   first depth check on the managed decoder (ticket 15)
#   tone-1k-96k.flac     1 kHz sine, 96 kHz / 16-bit     first rate check on the managed decoder (ticket 15)
#
# Levels: lavfi sources are mono and "-ac 2" upmixes through swresample, which applies -3 dB
# (0.707). So the ramp peaks at ~0.64 on disk, not 0.9, and the tone is quieter than written.
# Harmless: the tests self-calibrate against a sequential decode of the same file. Do not
# "fix" the decoders for it. (Verified from the raw s16 bytes, not through a decoder.)
#
# The two ticket-15 fixtures vary depth and rate one at a time, as a first check that the
# managed FLAC decoder handles the promised depths and rates. Ticket 14 fills the rest of the
# grid (16/24-bit x 44.1/48/96 kHz x mono/stereo).
set -euo pipefail
cd "$(dirname "$0")"

ffmpeg -y -loglevel error -f lavfi -i "sine=frequency=1000:sample_rate=44100:duration=2" \
  -ac 2 -sample_fmt s16 tone-1k.flac

ffmpeg -y -loglevel error -f lavfi -i "aevalsrc='0.9*t/2':s=44100:d=2" \
  -ac 2 -sample_fmt s16 ramp.flac

# CBR 320 kbps with LAME's Info tag, like 447 of the library's 654 MP3s (ticket 08 survey).
ffmpeg -y -loglevel error -f lavfi -i "sine=frequency=1000:sample_rate=44100:duration=2" \
  -ac 2 -c:a libmp3lame -b:a 320k tone-1k.mp3

ffmpeg -y -loglevel error -f lavfi -i "sine=frequency=1000:sample_rate=44100:duration=2" \
  -ac 2 -sample_fmt s32 tone-1k-24bit.flac

ffmpeg -y -loglevel error -f lavfi -i "sine=frequency=1000:sample_rate=96000:duration=2" \
  -ac 2 -sample_fmt s16 tone-1k-96k.flac

ls -l *.flac *.mp3
