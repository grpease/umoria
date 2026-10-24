// Scoreboard scenarios for score_dump (C++) and ScoreTraceTests (C#), which
// parses this file -- one list, so the two sides can't drift.
//
// Each scenario starts from an empty score file and enters its entries in
// order through recordNewHighScore(). An entry is
//   points:birth_date:gender(M/F):race:class:flags:died_from
// flags: '-' none, 'n' game.noscore = 2, 'p' panic_save. points is set as
// py.misc.max_exp with nothing else counted, so it is the entry's score.
SCORE_SCENARIO("ascending", "100:1:M:0:0:-:a Kobold|200:2:F:1:1:-:a Jackal|300:3:M:2:2:-:an Orc")
SCORE_SCENARIO("descending", "300:1:M:0:0:-:a Kobold|200:2:F:1:1:-:a Jackal|100:3:M:2:2:-:an Orc")
SCORE_SCENARIO("ties", "100:1:M:0:0:-:first|100:2:M:0:0:-:second|100:3:M:0:0:-:third")
SCORE_SCENARIO("saved_then_better", "100:7:M:0:0:-:(saved)|200:7:M:0:0:-:a Kobold")
SCORE_SCENARIO("saved_then_worse", "300:7:M:0:0:-:(saved)|200:7:M:0:0:-:a Kobold")
SCORE_SCENARIO("saved_then_equal", "200:7:F:3:2:-:(saved)|200:7:F:3:2:-:(saved)")
SCORE_SCENARIO("died_then_better", "100:7:M:0:0:-:a Kobold|200:7:M:0:0:-:a Kobold")
SCORE_SCENARIO("saved_other_class", "100:7:M:0:0:-:(saved)|200:7:M:0:1:-:a Kobold")
SCORE_SCENARIO("saved_other_gender", "100:7:M:0:0:-:(saved)|200:7:F:0:0:-:a Kobold")
SCORE_SCENARIO("saved_other_birth", "100:7:M:0:0:-:(saved)|200:8:M:0:0:-:a Kobold")
SCORE_SCENARIO("shift_drops_dup", "500:1:M:0:0:-:x|400:2:M:0:0:-:y|300:9:F:1:3:-:(saved)|200:3:M:0:0:-:z|100:9:F:1:3:-:(saved)|450:9:F:1:3:-:a Grip, Farmer Maggot")
SCORE_SCENARIO("shift_no_dup", "500:1:M:0:0:-:x|400:2:M:0:0:-:y|300:3:M:0:0:-:z|350:4:F:2:1:-:(saved)")
SCORE_SCENARIO("articles", "10:1:M:0:0:-:an Orc|20:2:M:0:0:-:a Kobold|30:3:M:0:0:-:ant lion|40:4:M:0:0:-:a  two spaces|50:5:M:0:0:-:Bob|60:6:M:0:0:-:a|70:7:M:0:0:-:an|80:8:M:0:0:-:A capital|90:9:F:0:0:-:abcdefghijklmnopqrstuvwx")
SCORE_SCENARIO("noscore_panic", "100:1:M:0:0:n:a Kobold|200:2:M:0:0:-:a Jackal|300:3:M:0:0:p:an Orc|50:4:F:0:0:-:a Rat")
SCORE_SCENARIO("all_noscore", "100:1:M:0:0:n:a Kobold|200:2:M:0:0:n:a Jackal")
SCORE_SCENARIO("zero_and_negative", "0:1:M:0:0:-:(alive and well)|-5:2:M:0:0:-:starvation|0:3:F:7:0:-:(end of input: saved)")
SCORE_SCENARIO("many", "0:100:M:0:0:-:(saved)|11947:101:F:1:1:-:a Cave spider|10894:102:M:2:2:-:a Cave spider|9841:103:F:3:3:-:a Cave spider|8788:104:M:4:4:-:a Cave spider|7735:105:F:5:5:-:(saved)|6682:100:M:6:0:-:a Cave spider|5629:101:F:7:1:-:a Cave spider|4576:102:M:0:2:-:a Cave spider|3523:103:F:1:3:-:a Cave spider|2470:104:M:2:4:-:(saved)|1417:105:F:3:5:-:a Cave spider|364:100:M:4:0:-:a Cave spider|12311:101:F:5:1:-:a Cave spider|11258:102:M:6:2:-:a Cave spider|10205:103:F:7:3:-:(saved)|9152:104:M:0:4:-:a Cave spider|8099:105:F:1:5:-:a Cave spider|7046:100:M:2:0:-:a Cave spider|5993:101:F:3:1:-:a Cave spider|4940:102:M:4:2:-:(saved)|3887:103:F:5:3:-:a Cave spider|2834:104:M:6:4:-:a Cave spider|1781:105:F:7:5:-:a Cave spider|728:100:M:0:0:-:a Cave spider|12675:101:F:1:1:-:(saved)|11622:102:M:2:2:-:a Cave spider|10569:103:F:3:3:-:a Cave spider|9516:104:M:4:4:-:a Cave spider|8463:105:F:5:5:-:a Cave spider|7410:100:M:6:0:-:(saved)|6357:101:F:7:1:-:a Cave spider|5304:102:M:0:2:-:a Cave spider|4251:103:F:1:3:-:a Cave spider|3198:104:M:2:4:-:a Cave spider|2145:105:F:3:5:-:(saved)|1092:100:M:4:0:-:a Cave spider|39:101:F:5:1:-:a Cave spider|11986:102:M:6:2:-:a Cave spider|10933:103:F:7:3:-:a Cave spider|9880:104:M:0:4:-:(saved)|8827:105:F:1:5:-:a Cave spider|7774:100:M:2:0:-:a Cave spider|6721:101:F:3:1:-:a Cave spider|5668:102:M:4:2:-:a Cave spider")
