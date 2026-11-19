offsets mm -10,-5,0,5,10; dirs side/front/back; speeds 3.4,9 m/s; bend 40 deg
| joint | pose (axis, deg) | bridge cells (box-edge cells, max) | swings | cells lost near joint off | on | joint cut off | joint cut on | touched off | touched on |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| LeftKnee | right 0 | 16 (0) | 30 | 1761 | 2026 | 11 | 7 | 30 | 30 |
| LeftKnee | right +40 | 11 (0) | 30 | 1336 | 1682 | 3 | 6 | 30 | 30 |
| LeftKnee | right -40 | 24 (2) | 30 | 1594 | 1670 | 10 | 9 | 30 | 30 |
| LeftKnee | forward +40 | 14 (0) | 30 | 1999 | 2133 | 12 | 11 | 30 | 30 |
| LeftKnee | forward -40 | 11 (0) | 30 | 1778 | 2018 | 7 | 8 | 30 | 30 |
| RightKnee | right 0 | 17 (0) | 30 | 1812 | 2176 | 2 | 0 | 30 | 30 |
| RightKnee | right +40 | 14 (0) | 30 | 2336 | 2475 | 1 | 4 | 30 | 30 |
| RightKnee | right -40 | 21 (2) | 30 | 1439 | 1950 | 1 | 1 | 30 | 30 |
| RightKnee | forward +40 | 15 (1) | 30 | 1539 | 1986 | 1 | 4 | 30 | 30 |
| RightKnee | forward -40 | 10 (0) | 30 | 1631 | 2128 | 1 | 5 | 30 | 30 |

swings where the bridge changes whether the joint is cut (off -> on):
| joint | pose | dir | offset mm | speed | cut off | cut on | near cells off | near cells on |
|---|---|---|---:|---:|---|---|---:|---:|
| LeftKnee | right 0 | front | 0 | 3.4 | True | False | 68 | 51 |
| LeftKnee | right 0 | front | 5 | 3.4 | True | False | 53 | 71 |
| LeftKnee | right 0 | front | 5 | 9 | False | True | 75 | 87 |
| LeftKnee | right 0 | front | 10 | 9 | True | False | 88 | 49 |
| LeftKnee | right 0 | back | -10 | 3.4 | True | False | 63 | 64 |
| LeftKnee | right 0 | back | 10 | 3.4 | True | False | 83 | 32 |
| LeftKnee | right +40 | front | -10 | 3.4 | False | True | 22 | 87 |
| LeftKnee | right +40 | front | 5 | 9 | False | True | 94 | 102 |
| LeftKnee | right +40 | back | 10 | 9 | False | True | 85 | 86 |
| LeftKnee | right -40 | side | -5 | 3.4 | True | False | 54 | 57 |
| LeftKnee | forward +40 | side | -5 | 3.4 | True | False | 62 | 39 |
| LeftKnee | forward +40 | front | 5 | 3.4 | True | False | 55 | 73 |
| LeftKnee | forward +40 | back | 10 | 3.4 | False | True | 71 | 89 |
| LeftKnee | forward -40 | front | 10 | 3.4 | False | True | 39 | 86 |
| LeftKnee | forward -40 | back | 10 | 3.4 | False | True | 18 | 84 |
| LeftKnee | forward -40 | back | 10 | 9 | True | False | 90 | 61 |
| RightKnee | right 0 | side | 0 | 9 | True | False | 133 | 90 |
| RightKnee | right 0 | front | 5 | 9 | True | False | 183 | 85 |
| RightKnee | right +40 | front | -5 | 3.4 | False | True | 94 | 140 |
| RightKnee | right +40 | front | 5 | 3.4 | False | True | 61 | 85 |
| RightKnee | right +40 | back | 10 | 9 | False | True | 40 | 97 |
| RightKnee | right -40 | front | 10 | 3.4 | True | False | 49 | 64 |
| RightKnee | right -40 | front | 5 | 9 | False | True | 73 | 95 |
| RightKnee | forward +40 | front | 0 | 3.4 | False | True | 73 | 170 |
| RightKnee | forward +40 | front | 5 | 3.4 | False | True | 53 | 76 |
| RightKnee | forward +40 | front | 10 | 3.4 | False | True | 69 | 55 |
| RightKnee | forward -40 | side | 5 | 3.4 | False | True | 75 | 83 |
| RightKnee | forward -40 | side | 5 | 9 | False | True | 66 | 99 |
| RightKnee | forward -40 | front | -5 | 3.4 | False | True | 66 | 82 |
| RightKnee | forward -40 | front | 5 | 3.4 | True | False | 68 | 66 |
| RightKnee | forward -40 | front | 0 | 9 | False | True | 88 | 151 |
| RightKnee | forward -40 | front | 5 | 9 | False | True | 65 | 97 |
