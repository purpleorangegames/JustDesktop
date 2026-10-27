#!/bin/bash
# renders the SVG icons to the PNGs the game loads, at twice the size they are drawn
cd "$(dirname "$0")"
for f in $(find . -name "*.svg"); do
  case "$f" in
    ./Start.svg|./Round.svg|./RoundLine.svg|./RoundSmall.svg|./RoundSmallLine.svg) size=48 ;;
    ./Circle.svg|./CircleLine.svg|./Check.svg|./Box.svg|./BoxLine.svg) size=48 ;;
    ./Window/*) size=32 ;;
    *) size=40 ;;
  esac
  out=../Textures/UI/JustDesktop/${f#./}
  mkdir -p "$(dirname "$out")"
  rsvg-convert -w $size -h $size "$f" -o "${out%.svg}.png"
done
