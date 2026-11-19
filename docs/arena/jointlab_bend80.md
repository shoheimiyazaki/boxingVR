offsets mm -10,-5,0,5,10; dirs side/front/back; speeds 3.4,9 m/s; bend 80 deg
| joint | pose (axis, deg) | bridge cells (box-edge cells, max) | swings | cells lost near joint off | on | joint cut off | joint cut on | touched off | touched on |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| LeftElbow | right 0 | 0 (0) | 30 | 773 | 793 | 5 | 5 | 30 | 30 |
| LeftElbow | right +80 | 0 (0) | 30 | 455 | 491 | 3 | 3 | 30 | 30 |
| LeftElbow | right -80 | 0 (0) | 30 | 554 | 571 | 1 | 1 | 30 | 30 |
| LeftElbow | forward +80 | 0 (0) | 30 | 436 | 476 | 2 | 2 | 30 | 30 |
| LeftElbow | forward -80 | 0 (0) | 30 | 706 | 695 | 3 | 3 | 30 | 30 |
| LeftKnee | right 0 | 16 (0) | 30 | 1722 | 1868 | 11 | 10 | 30 | 30 |
| LeftKnee | right +80 | 11 (4) | 30 | 1526 | 1780 | 5 | 7 | 30 | 30 |
| LeftKnee | right -80 | 17 (2) | 30 | 1235 | 1376 | 7 | 8 | 30 | 30 |
| LeftKnee | forward +80 | 17 (0) | 30 | 1365 | 1591 | 10 | 10 | 30 | 30 |
| LeftKnee | forward -80 | 18 (3) | 30 | 1808 | 1815 | 15 | 14 | 30 | 30 |
| LeftAnkle | right 0 | 1 (0) | 30 | 1618 | 1672 | 1 | 1 | 30 | 30 |
| LeftAnkle | right +80 | 0 (0) | 30 | 1541 | 1701 | 2 | 2 | 30 | 30 |
| LeftAnkle | right -80 | 4 (0) | 30 | 1561 | 1571 | 1 | 1 | 30 | 30 |
| LeftAnkle | forward +80 | 8 (2) | 30 | 1874 | 1802 | 1 | 1 | 30 | 30 |
| LeftAnkle | forward -80 | 7 (1) | 30 | 1925 | 1937 | 1 | 1 | 30 | 30 |
| LeftHip | right 0 | 8 (5) | 30 | 768 | 687 | 0 | 0 | 30 | 30 |
| LeftHip | right +80 | 6 (3) | 30 | 377 | 347 | 0 | 0 | 30 | 30 |
| LeftHip | right -80 | 8 (4) | 30 | 456 | 526 | 0 | 0 | 30 | 30 |
| LeftHip | forward +80 | 0 (0) | 30 | 400 | 310 | 0 | 0 | 30 | 30 |
| LeftHip | forward -80 | 9 (6) | 30 | 474 | 508 | 0 | 0 | 30 | 30 |
| LeftShoulder | right 0 | 10 (2) | 30 | 663 | 749 | 0 | 0 | 30 | 30 |
| LeftShoulder | right +80 | 12 (2) | 30 | 1153 | 1103 | 0 | 0 | 30 | 30 |
| LeftShoulder | right -80 | 12 (0) | 30 | 531 | 656 | 0 | 0 | 30 | 30 |
| LeftShoulder | forward +80 | 13 (3) | 30 | 830 | 956 | 0 | 0 | 30 | 30 |
| LeftShoulder | forward -80 | 7 (0) | 30 | 760 | 814 | 0 | 0 | 30 | 30 |
| Spine | right 0 | 3 (0) | 30 | 1513 | 2109 | 0 | 0 | 30 | 30 |
| Spine | right +80 | 27 (13) | 30 | 1219 | 1586 | 0 | 0 | 30 | 30 |
| Spine | right -80 | 30 (8) | 30 | 1788 | 1908 | 0 | 0 | 30 | 30 |
| Spine | forward +80 | 23 (2) | 30 | 1423 | 1623 | 0 | 0 | 30 | 30 |
| Spine | forward -80 | 25 (2) | 30 | 1516 | 1845 | 0 | 0 | 30 | 30 |

swings where the bridge changes whether the joint is cut (off -> on):
| joint | pose | dir | offset mm | speed | cut off | cut on | near cells off | near cells on |
|---|---|---|---:|---:|---|---|---:|---:|
| LeftKnee | right 0 | side | -5 | 3.4 | True | False | 53 | 59 |
| LeftKnee | right 0 | front | 0 | 3.4 | False | True | 44 | 86 |
| LeftKnee | right 0 | front | 5 | 3.4 | False | True | 54 | 70 |
| LeftKnee | right 0 | back | 0 | 3.4 | True | False | 60 | 105 |
| LeftKnee | right 0 | back | 5 | 3.4 | True | False | 78 | 46 |
| LeftKnee | right +80 | side | 5 | 9 | False | True | 104 | 123 |
| LeftKnee | right +80 | front | 0 | 9 | False | True | 43 | 84 |
| LeftKnee | right +80 | back | 10 | 3.4 | True | False | 78 | 131 |
| LeftKnee | right +80 | back | 5 | 9 | False | True | 73 | 137 |
| LeftKnee | right -80 | front | -10 | 3.4 | False | True | 60 | 76 |
| LeftKnee | forward -80 | back | -10 | 3.4 | True | False | 62 | 65 |
