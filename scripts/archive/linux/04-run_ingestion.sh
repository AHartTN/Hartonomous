#!/bin/bash
export UCD_DB_HOST=localhost
export UCD_DB_USER=postgres
export UCD_DB_PASSWORD=postgres
export UCD_DB_NAME=hartonomous
export UCD_DB_PORT=5432

./build/linux-release-max-perf/UCDIngestor/ucd_ingestor \
  UCDIngestor/data/ucd.all.flat.xml \
  UCDIngestor/data/allkeys.txt \
  UCDIngestor/data/confusables.txt \
  UCDIngestor/data/emoji-sequences.txt \
  UCDIngestor/data/emoji-zwj-sequences.txt
