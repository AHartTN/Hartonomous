#!/usr/bin/env bash
# Concatenates SQL files for PostgreSQL extensions (replaces \i commands)

set -e

INPUT_FILE=$1
OUTPUT_FILE=$2
BASE_DIR=$(dirname "$INPUT_FILE")

if [ -z "$INPUT_FILE" ] || [ -z "$OUTPUT_FILE" ]; then
    echo "Usage: $0 <input_sql> <output_sql>"
    exit 1
fi

echo "-- Generated from $INPUT_FILE" > "$OUTPUT_FILE"

while IFS= read -r line || [[ -n "$line" ]]; do
    if [[ $line =~ ^\\i\ \'(.*)\' ]]; then
        INCLUDE_FILE="${BASH_REMATCH[1]}"
        # If INCLUDE_FILE is relative, it should be relative to the BASE_DIR
        # but check if BASE_DIR is already in the INCLUDE_FILE path to avoid double nesting
        FULL_PATH="$BASE_DIR/$INCLUDE_FILE"
        
        echo "-- Including $INCLUDE_FILE" >> "$OUTPUT_FILE"
        if [ -f "$FULL_PATH" ]; then
            cat "$FULL_PATH" >> "$OUTPUT_FILE"
        else
            echo "Error: Could not find $FULL_PATH"
            exit 1
        fi
        echo "" >> "$OUTPUT_FILE"
    else
        echo "$line" >> "$OUTPUT_FILE"
    fi
done < "$INPUT_FILE"