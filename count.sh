#!/bin/bash

# Script: count.sh
# Description: Counts from 1 to a given number, sleeping 1 second between each number

# Check if a number was provided
if [ -z "$1" ]; then
    echo "Usage: $0 <number>"
    echo "Example: $0 10"
    exit 1
fi

# Validate that the argument is a positive integer
if ! [[ "$1" =~ ^[0-9]+$ ]]; then
    echo "Error: Please provide a positive integer"
    exit 1
fi

COUNT_TO=$1

echo "Counting from 1 to $COUNT_TO..."

for ((i=1; i<=COUNT_TO; i++)); do
    echo "$i"
    sleep 1
done

echo "Done!"
