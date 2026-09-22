#!/usr/bin/env bash
# Regenerates the checked-in compressed fixtures. Run from this directory with ffmpeg on PATH.
#
# Fixtures are checked in rather than generated at test time, by decision (ticket 06, ADR 0002):
# a cell that skips because ffmpeg is absent is green while asserting nothing. This script is
# the provenance. Every fixture is 2 s, 44.1 kHz, stereo, so no resampling or channel mapping
# happens on the way in and the decoder is the only thing under test.
#
#   tone-1k.flac   1 kHz sine            purity and completeness
#   ramp.flac      0 -> 0.9 linear ramp  seek landing: the sample value IS the position
#   ramp.ogg       same ramp, Vorbis     defect C (seek before first read) and seek landing
#
# Levels: lavfi sources are mono and "-ac 2" upmixes through swresample, which applies -3 dB
# (0.707). So the ramp peaks at ~0.64 on disk, not 0.9, and the tone is quieter than written.
# Harmless: the tests self-calibrate against a sequential decode of the same file. Do not
# "fix" the decoders for it. (Verified from the raw s16 bytes, not through a decoder.)
#
# Ticket 14 fills the rest of the grid (16/24-bit x 44.1/48/96 kHz x mono/stereo).
set -euo pipefail
cd "$(dirname "$0")"

ffmpeg -y -loglevel error -f lavfi -i "sine=frequency=1000:sample_rate=44100:duration=2" \
  -ac 2 -sample_fmt s16 tone-1k.flac

ffmpeg -y -loglevel error -f lavfi -i "aevalsrc='0.9*t/2':s=44100:d=2" \
  -ac 2 -sample_fmt s16 ramp.flac

# -page_duration: ffmpeg's ogg muxer defaults to 1 s pages, and NVorbis 0.10.5 throws
# "GranulePos mismatch" on ANY seek into such a file (sequential decode is fine). oggenc-style
# short pages seek cleanly. Recorded against ticket 08; found while pinning in ticket 07.
ffmpeg -y -loglevel error -f lavfi -i "aevalsrc='0.9*t/2':s=44100:d=2" \
  -ac 2 -c:a libvorbis -q:a 6 -page_duration 10000 ramp.ogg

ls -l *.flac *.ogg
