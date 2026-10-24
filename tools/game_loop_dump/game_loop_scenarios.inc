// The M15 game loop scenarios, shared by tools/game_loop_dump (the real C++
// game) and the C# GameLoopTraceTests. One SCENARIO per line:
//   SCENARIO("name", seed, "run 0 keys", "run 1 keys", ...)
// Each run is one start of the program in the same game directory; the
// first has no save file. Keys: ~ <ESCAPE>, $ <RETURN>, ^x control-x,
// `x a literal x, anything else itself. A run ends when its keys do (end
// of input: the game saves and exits).
SCENARIO("smoke", 12345, " am~aTester$ ")
SCENARIO("screens", 4242, " x?~? ~a?~f  ~z?~aScreens$ ?~C~^PV=~v~^V~M~L~/@/k/w x5          i~e~")
SCENARIO("town_shop_descend", 4242, " am~aShopper$ 336666666662pa47$~~7441>22226666M~i~e~S2sl5          ~#54R5$R*$ .6T4EaFj6D6B6o6c6-4f~qr~a~u~w~t~x~ba~b~GmpWL~<>/T{~d~xx~C~^P ")
SCENARIO("save_restore", 4242, " am~aSaver$ 66^P^X", " ^P^P~C~22233^X", " 1")
SCENARIO("quit_tomb", 999, " bf~aQuitter$ 66^Ky$ $ $ $ ")
SCENARIO("mage", 31337, " am~bMage$  Ga~maa6maa~ba ~R9$maa4^PC~", " maa6")
SCENARIO("priest", 777, " cm~cPriest$  Gpaa~pab~ba ~R9$^P")
SCENARIO("roguelike", 4242, " am~aRogue$ =      y~hjklyubn5lx.~W~R3$#S~?~^P", " llll=      n~")
SCENARIO("counts", 4242, " am~aCounter$ #32#12``6#5i^p^9#~#2^V~#3s2")
SCENARIO("long_rest", 4242, " am~aSleeper$ 336666666662~7441>R1500$R1500$R1500$R1500$ ")
