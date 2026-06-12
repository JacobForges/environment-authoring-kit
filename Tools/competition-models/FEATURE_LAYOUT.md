# Feature layouts (C# encoder reference)

## Setup `features[100]`

| Offset | Len | Field |
|--------|-----|--------|
| 0 | 3 | build one-hot |
| 3 | 3 | height one-hot |
| 6 | 3 | style one-hot |
| 9 | 4 | color one-hot |
| 13 | 4 | kit one-hot |
| 17 | 3 | race one-hot |
| 20 | 3 | jump one-hot |
| 23 | 3 | stuck one-hot |
| 26 | 3 | risk one-hot |
| 29 | 3 | focus one-hot |
| 32 | 3 | fall one-hot |
| 35 | 16 | name hash noise |
| 51 | 49 | reserved zero |

## Chat `text_features[64]`

Word hash buckets + char trigram hash + **direct-agent flag** at [62] (set when line does **not** start with `@`) + normalized length at [63].

## Gameplay `state[96]`

See `trained/gameplay_manifest.json` → `obsBlocks`.

## Legacy policy `state[42]`

18 obs + 24 z — superseded by gameplay v2.
