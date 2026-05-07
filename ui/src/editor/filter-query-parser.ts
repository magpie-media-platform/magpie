
import {LRParser} from "@lezer/lr"
import {specializeKeyword} from "./keywords"
export const parser = LRParser.deserialize({
  version: 14,
  states: ")YOVQPOOObQPO'#ChOOQO'#D^'#D^OOQO'#DZ'#DZOYQPO'#DYOOQO'#DY'#DYO!lQPO'#DXO!zQPO'#DWOVQPO'#CgQOQPOOOOQO'#Cj'#CjOOQO'#Cn'#CnOOQO'#Cr'#CrOOQO'#Ct'#CtO#VQPO,59SOOQO,59S,59SO#bQPO,59SOOQO'#Cx'#CxO#gQPO,59cOOQO,59t,59tOVQPO'#DRO#uQPO,59sOVQPO'#DSO$TQPO,59rO$`QPO,59ROOQO'#Ck'#CkOOQO1G.n1G.nO#VQPO'#CvOOQO1G.}1G.}O$eQPO1G.}OOQO,59m,59mOOQO-E7P-E7POOQO,59n,59nOOQO-E7Q-E7QOOQO1G.m1G.mO$sQPO,59bO${QPO'#CkOOQO'#C}'#C}OOQO'#Dh'#DhO%|QPO'#DgO&XQPO'#DfO$eQPO'#C|O&aQPO7+$iO#VQPO'#DOO&fQPO1G.|OOQO1G.|1G.|OOQO'#C{'#C{O#VQPO,59fO$eQPO'#DPO&nQPO,5:RO$eQPO'#DQO&yQPO,5:QO'RQPO,59hOOQO<<HT<<HTOOQO,59j,59jOOQO-E6|-E6|OOQO7+$h7+$hOOQO1G/Q1G/QOOQO,59k,59kOOQO-E6}-E6}OOQO,59l,59lOOQO-E7O-E7OOOQO1G/S1G/S",
  stateData: "'g~OyOS~OPSO]PO!PWO~OSZOTZOUZOV[OW]OXaOcZOdZOeZOg[Oi]OmaO!RYO!SYO!TYO!UYO!VYO!WYO~OQdOR{Xw{X!O{X~ORfOwzX!OzX~O]iO`iOaiO~O!PkO~O]iO`iOaiO!PmO~OQdOR{aw{a!O{a~ORfOwza!Oza~O!OrO~O]tO`iOaiO!PyO~O!O}O!X{O~OS!OOT!OOU!OOc!OOd!OOe!OO!R!OO!S!OO!T!OO!U!OO!V!OO!W!OOQ_XR_X!O_X~OQ!QOR!ZX!O!ZX~OR!SO!O!YX~O!O!VO~O!O!YO!X{O~OQ!QOR!Za!O!Za~OR!SO!O!Ya~O!O!`O~Ocdemgi!U!W!S!T!V!Ra!U~",
  goto: "%`!]PPPPPPPPPPP!^!eP!l!oPP!lPPP#UP#XP#[!e#_P#b#h#b#b#k#q#w#}$TPPP$Z$a$h$pPP!^PPPPPPP$y%P%WZROSWdfZQOSWdfR^PQj^QlbQskWumy!Q!SQ!W{R!Z!PR_PR`PRj`RbPXvmy!Q!SR!PtQ|sR!X|Q!RwR!]!RQ!TxR!_!TQeURoeQgVRqgQXORhWSVOWRpfUUOWfRndWTOWdfRcSQzmR!UySxmyR!^!SUwmy!SR![!Q",
  nodeNames: "⚠ not and or contains startswith endswith null in has Query ParenExpr ScalarExpr Identifier CompareOp Value String Number StringOp NotContains NotStartsWith NotEndsWith NullOp NotNull InOp NotIn ValueList CollectionExpr CollectionOp NotHas ItemField ItemOp ItemGroup ItemValue",
  maxTerm: 58,
  skippedNodes: [0],
  repeatNodeCount: 5,
  tokenData: "+[~RaXY!WYZ!W]^!Wpq!Wqr!irs'lxy)Yyz)_|})d}!O)i!Q![)o!^!_*Y!_!`*g!`!a*l!c!}*y#R#S*y#T#o*y~!]Sy~XY!WYZ!W]^!Wpq!W~!lV!_!`#R#V#W#W#X#Y$X#[#]%Y#]#^%k#b#c%v#g#h&_~#WO!S~~#ZP#c#d#^~#aP#b#c#d~#gP#h#i#j~#mP#T#U#p~#sP#]#^#v~#yP#b#c#|~$PP#g#h$S~$XOc~~$[P#b#c$_~$bP#W#X$e~$hP#g#h$k~$nP#k#l$q~$tP#]#^$w~$zP#h#i$}~%QP#[#]%T~%YOe~~%]P#T#U%`~%cP#g#h%f~%kOm~~%nP#b#c%q~%vOi~~%yP#i#j%|~&PP#`#a&S~&VP#`#a&Y~&_Og~~&bP#h#i&e~&hP#T#U&k~&nP#f#g&q~&tP#h#i&w~&zP#g#h&}~'QP#k#l'T~'WP#]#^'Z~'^P#h#i'a~'dP#[#]'g~'lOd~~'oVOr'lrs(Us#O'l#O#P(Z#P;'S'l;'S;=`)S<%lO'l~(ZO`~~(^RO;'S'l;'S;=`(g;=`O'l~(jWOr'lrs(Us#O'l#O#P(Z#P;'S'l;'S;=`)S;=`<%l'l<%lO'l~)VP;=`<%l'l~)_O!P~~)dO!O~~)iO!X~~)lP!Q![)o~)tQa~!O!P)z!Q![)o~)}P!Q![*Q~*VPa~!Q![*Q~*_P!V~!_!`*b~*gO!W~~*lO!R~~*qP!T~!_!`*t~*yO!U~~+OS]~!Q![*y!c!}*y#R#S*y#T#o*y",
  tokenizers: [0],
  topRules: {"Query":[0,10]},
  specialized: [{term: 13, get: (value: string) => (specializeKeyword(value) << 1), external: specializeKeyword}],
  tokenPrec: 284
})
