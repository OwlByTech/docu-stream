#!/bin/bash
grpcurl -plaintext -proto ../proto/word.proto \
  -d '{
        "word": {
          "header": [
            {"key": "Company Name", "value": "OwlByTech"}
          ],
          "body": [
            {"key": "Company Name", "value": "OwlByTech"}
          ]
        }
      }' \
  0.0.0.0:3000 word.Word.Apply
