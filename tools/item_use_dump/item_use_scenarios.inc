// Item-use scenarios (M11), shared by item_use_dump.cpp (#included into its
// scenario array) and ItemUseTraceTests.cs (parsed as text), so the two
// sides can't drift. Fields: name, seed, race, male, class, position, depth,
// setup bits, experience, monsters, script -- see item_use_dump.cpp. Only P
// / DARK, T and the S_* bits, `|`, numbers, true/false and strings are
// allowed here; the C# parser knows no more than that.

// ---- eating ----
{"eat_cures", 12345, 0, true, 0, P, 5, T | S_HURT | S_AILING, 0, "", "P5:1 P6:1 P7:1 P8:1 P12:1 P13:1 P14:1 P20:1 x20 e%6. e%5. e%7. f5 e%8. e%12. e%13. e%14. e%20."},
{"eat_ailments", 31337, 0, true, 0, P, 5, T, 0, "", "P0:1 P1:1 P2:1 P3:1 P9:1 P10:1 e%0. e%2. e%3. e%9. e%10. e%1."},
{"eat_restore", 777, 0, true, 0, P, 5, T | S_AILING, 0, "", "P11:1 P15:2 e%11. e%15. e%15. P11:1 e%11."},
{"eat_known_twice", 4242, 0, true, 2, P, 5, T | S_HURT, 0, "", "P12:3 e%12. e%12. e%12. eb*3"},
{"eat_bloated", 987654321, 0, true, 0, P, 5, T, 0, "", "F14000 ea F9000 ea F-50 ea P25:2 e%25. F14990 e%25."},
{"eat_poisonous_death", 12345, 0, true, 0, P, 5, T, 0, "", "H3 P9:1 e%9. w"},
{"eat_refusals", 31337, 0, true, 0, P, 5, T, 0, "", "e~ K0*5 e K0*20 e"},
{"eat_hallucination", 4242, 0, true, 0, P, 5, T, 0, "", "P4:1 e%4."},

// ---- quaffing ----
{"quaff_stats", 777, 0, true, 0, P, 5, T | S_AILING, 0, "", "P225:1 P227:1 P228:1 P230:1 P231:1 P233:1 P234:1 P236:1 P243:1 P251:1 P252:1 P253:1 q%225. q%227. q%228. q%230. q%231. q%233. q%234. q%236. q%243. q%251. q%252. q%253."},
{"quaff_losses", 4242, 0, true, 0, P, 5, T, 0, "", "P226:1 P229:1 P232:1 P235:1 q%226. q%229. q%232. q%235."},
{"quaff_losses_sustained", 987654321, 0, true, 0, P, 5, T | S_SUSTAIN, 0, "", "P226:1 P229:1 P232:1 P235:1 q%226. q%229. q%232. q%235."},
{"quaff_heal", 12345, 0, true, 0, P, 5, T | S_HURT | S_AILING | S_BLIND, 0, "", "P237:1 P240:1 P241:1 P242:1 P222:1 P223:1 P224:1 q%237. q%240. q%241. q%242. q%222. q%223. q%224."},
{"quaff_status", 31337, 0, true, 0, P, 5, T, 0, "", "P245:1 P246:1 P247:1 P248:1 P249:2 P250:2 q%245. q%246. q%247. q%248. q%249. q%249. q%250. q%250."},
{"quaff_exp", 777, 0, true, 1, P, 5, T, 3000, "", "P244:2 P254:2 q%244. q%254. q%244. q%254. P260:1 q%260."},
{"quaff_misc", 4242, 0, true, 1, P, 5, T | S_AILING | S_MANA, 3000, "", "P255:1 P256:1 P257:1 P258:1 P259:1 P261:1 P262:1 P263:1 P264:1 P265:1 P266:1 P267:1 M0 q%255. q%256. q%257. q%258. q%259. q%261. q%262. q%263. q%264. q%265. q%266. q%267."},
{"quaff_refusals", 987654321, 0, true, 0, P, 5, T, 0, "", "q P237:1 q~ K0*30 q"},
{"quaff_tried", 12345, 0, true, 0, P, 5, T, 0, "", "P227:3 q%227. P176:1 r%176.%227. q%227."},

// ---- reading ----
{"read_refusals", 31337, 0, true, 0, P, 5, T, 0, "", "r P176:1 x5 r x0 f5 r f0 r~ K0*30 r"},
{"read_dark", 777, 0, true, 0, DARK, 5, T | S_DARK, 0, "", "P176:1 r"},
{"read_enchant", 4242, 0, true, 0, P, 5, T | S_WEAPON | S_CURSED, 0, "", "P173:3 P174:3 P175:3 P212:2 P214:2 r%173.*3 r%174.*3 r%175.*3 r%212.*2 r%214.*2"},
{"read_enchant_none", 987654321, 0, true, 0, P, 5, T, 0, "", "P173:1 P174:1 P175:1 P212:1 P213:1 P214:1 P215:1 r%173. r%174. r%175. r%212. r%213. r%214. r%215."},
{"read_curse", 12345, 0, true, 0, P, 5, T | S_WEAPON, 0, "", "X91:30 X103:25 P213:1 P215:3 r%213. r%215.*3"},
{"read_identify", 31337, 0, true, 0, P, 5, T | S_UNKNOWN, 0, "", "P176:4 r%176.%243. r%176.~ r%176.%176. r%176.%251."},
{"read_detect_light", 777, 0, true, 0, P, 5, T | S_TRAPS, 0, "34@9,23 77@11,30z30000", "P181:1 P189:1 P192:1 P193:1 P194:1 P197:1 P201:1 P208:1 P191:1 P209:1 P210:1 r%181. r%189. r%192. r%193. r%194. r%197. r%201. r%208. r%191. r%209. r%210."},
{"read_monsters", 4242, 0, true, 0, P, 5, T, 0, "16@11,21 77@11,25z30000 63@9,24", "P184:1 P188:1 P190:1 P202:1 P211:1 P216:1 P200:1 r%188. r%190. r%184. r%202. r%211. r%216. r%200. w*2"},
{"read_genocide", 987654321, 0, true, 0, P, 5, T, 0, "16@11,25 16@11,30 77@5,40", "P207:2 r%207.k r%207.~"},
{"read_traps_doors", 12345, 0, true, 0, P, 5, T, 0, "", "P203:1 P204:1 P205:1 r%203. r%205. r%204."},
{"read_recharge", 31337, 0, true, 1, P, 5, T | S_STAFF, 0, "", "P206:3 r%206.%293. r%206.~ r%206.%282."},
{"read_teleports", 777, 0, true, 0, P, 5, T, 0, "", "P185:2 P186:1 P220:1 P217:1 P218:1 P219:1 r%185. r%185. r%186. r%220. r%217. r%218. r%219. w*3"},
{"read_teleport_level", 4242, 0, true, 0, P, 5, T, 0, "", "P187:1 r%187. w"},
{"read_destruction", 987654321, 0, true, 0, P, 5, T, 0, "77@11,25 16@5,40", "P221:1 r%221."},
{"read_remove_curse", 12345, 0, true, 0, P, 5, T | S_CURSED | S_WEAPON, 0, "", "P180:2 r%180. r%180."},

// ---- staffs ----
{"staff_uses_a", 31337, 0, true, 1, P, 5, T | S_HURT | S_AILING | S_TRAPS, 40000, "77@11,25z30000 63@9,24z30000", "P293:1:3 P294:1:3 P295:1:3 P296:1:3 P297:1:3 P303:1:3 P304:1:3 P305:1:3 P306:1:3 P307:1:3 P308:1:3 u%293. u%294. u%295. u%296. u%297. u%303. u%304. u%305. u%306. u%307. u%308. u%307."},
{"staff_uses_b", 777, 0, true, 1, P, 5, T | S_HURT | S_AILING | S_CURSED, 40000, "77@11,25z30000 63@9,24 16@13,22", "P309:1:2 P310:1:2 P311:1:2 P312:1:2 P313:1:2 P314:1:2 P315:1:2 P316:1:2 u%309. u%310. u%311. u%312. u%313. u%314. u%315. u%316. u%312."},
{"staff_quakes", 4242, 0, true, 1, P, 5, T, 40000, "77@11,25", "P298:1:2 P299:1:2 P300:1:2 P302:1:1 u%299. u%300. u%298. u%302."},
{"staff_failures", 987654321, 0, true, 0, P, 30, T, 0, "", "P293:1:20 u%293.*8 f5 u%293.*4 f0 P294:1:0 u%294.*6 D293 u%293.*3"},
{"staff_refusals", 12345, 0, true, 0, P, 5, T, 0, "", "u P293:1:2 u~ K0*30 u"},

// ---- wands ----
{"wand_bolts", 31337, 0, true, 1, P, 5, T, 40000, "77@11,26z30000 16@11,30z30000 63@11,34z30000", "P269:1:3 P270:1:3 P271:1:3 P272:1:3 P282:1:3 P287:1:2 P288:1:2 P289:1:2 P290:1:2 P291:1:2 a%269.6 a%270.6 a%271.6 a%272.6 a%282.6 a%287.6 a%288.6 a%289.6 a%290.6 a%291.6"},
{"wand_status", 777, 0, true, 1, P, 5, T, 40000, "77@11,26z30000 16@11,30 63@9,20", "P274:1:2 P275:1:2 P276:1:2 P277:1:2 P278:1:2 P279:1:2 P280:1:2 P284:1:2 P285:1:2 a%275.6 a%276.6 a%277.6 a%278.6 a%279.6 a%280.6 a%284.6 a%274.6 a%285.8 a%285.6"},
{"wand_terrain", 4242, 0, true, 1, P, 5, T | S_RUBBLE | S_TRAPS, 40000, "", "P273:1:3 P281:1:3 P283:1:3 P286:1:3 a%273.6 a%273.8 a%281.9 a%283.4 a%286.7 a%286.3"},
{"wand_wonder", 987654321, 0, true, 1, P, 5, T, 40000, "77@11,26z30000 16@11,30z30000", "P292:1:10 a%292.6*10"},
{"wand_misc", 12345, 0, true, 0, P, 5, T, 0, "77@11,26z30000", "a P282:1:5 a~ a%282.~ f5 a%282.6*4 f0 P271:1:0 a%271.6*4 D282 a%282.6*3"},

// ---- throwing ----
{"throw_objects", 31337, 0, true, 0, P, 5, T, 0, "77@11,24z30000", "P28:1 t%28.6 P268:3 t%268.6*3 P80:10 t%80.6*3 t%80.4 t%80.8 t%80.~ t~"},
{"throw_bows", 777, 0, true, 4, P, 5, T, 0, "77@11,28z30000 16@11,40z30000", "X74:22 P80:20 t%80.6*6 X79:22 P82:10 t%82.6*3 X77:22 P81:10 t%81.6*3 t%81.2 t%81.8"},
{"throw_dark", 4242, 0, true, 0, DARK, 5, T | S_DARK, 0, "77@11,124z30000", "P80:20 t%80.6*8"},
{"throw_confused", 987654321, 0, true, 0, P, 5, T, 0, "77@11,22", "P80:10 f10 t%80.6*5"},
{"throw_kill", 12345, 0, true, 0, P, 5, T, 0, "17@11,22h1 17@11,24h1", "P268:5 t%268.6*4"},
{"throw_refusals", 31337, 0, true, 0, P, 5, T, 0, "", "t~ ta~ K0*30 t"},

// ---- inventory commands ----
{"inv_wear_basic", 777, 0, true, 0, P, 5, T, 0, "", "Iwb~ Iwb~ Iwb~ Ie~ Ii~ It~ Ix~ Ix~ Ita~ Iwc~"},
{"inv_rings", 4242, 0, true, 0, P, 5, T, 0, "", "P132:1 P133:1 P134:1 P135:1 P163:1 P91:1 Iw%132.~ Iw%133.~ Iw%134.xr~ n Iw%135.Ll~ y Iw%91.~ Iw~ Iw%163.~ IwBr~"},
{"inv_cursed", 987654321, 0, true, 0, P, 5, T, 0, "", "P37:1 P38:1 Iw%37.~ Z22 Iw%38.~ Ita~ Ix~ P145:1 Iw%145.~ Itb~ X103:25 Z25 P105:1 Iw%105.~"},
{"inv_drop", 12345, 0, true, 0, P, 5, T, 0, "", "Ida~ m6 A1 Ida~ m6 A-1 Ida~ A0 Ida~ Idb~ m6 Id/a~ m6 n IdB~ y IdB~"},
{"inv_drop_everything", 31337, 0, true, 0, P, 5, T, 0, "", "A1 Ida~ m6 Ida~ m6 Ida~ m6 Ida~ m6 Ida~ m6 Ida~ m6 Ida~"},
{"inv_takeoff_full", 777, 0, true, 0, P, 5, T, 0, "", "Iwb~ P28:1 P29:1 P30:1 P31:1 P33:1 P34:1 P35:1 P36:1 P37:1 P38:1 P39:1 P40:1 P41:1 P42:1 P43:1 P44:1 P45:1 P46:1 Ita~"},
{"inv_screens", 4242, 0, true, 0, P, 5, T, 0, "", "Iiwb~ Ie?~ Iw*b~ It*~ Iq~ Ii~ Ie~"},
{"inv_inscribe", 987654321, 0, true, 0, P, 5, T, 0, "", "{a:hello {b:1 {b:7 {~:x Iw7~ {c:2 Id2~"},
{"inv_nothing", 777, 0, true, 0, P, 5, T, 0, "", "K1*8 Iw~ It~ Ix~ Id~"},
{"inv_heavy", 12345, 0, true, 1, P, 5, T, 0, "", "P41:1 Iw%41.~ Ix~ Ix~"},
{"inv_magic_items", 31337, 0, true, 0, P, 5, T, 0, "", "G37:40 G103:40 G140:40 G163:40 G75:40 D37 Iw%37.~ Iw%103.~ Iw%140.~ Iw%163.~ Iw%75.~"},
{"inv_pickup", 777, 0, true, 0, P, 5, T, 0, "", "O0,1:237 O0,2:80 m6 m6"},
