#!/bin/bash
rndstr () { echo -n $(head -c 24 /dev/urandom | base64 | tr -d '+/='); }

#INSERT
curl "http://10.3.3.60:5000/project/0x2" -H 'Content-Type: application/json' -d '{"t":"C1proj-'$(rndstr)'","d":"A project for '$(rndstr)'","x":"customdata '$(rndstr)'","tg":["tagC","tagD"]}'

#GET
for uid in $(curl "http://10.3.3.60:5000/projects/0x2" 2>/dev/null | grep -oE 'uid":"0x[0-9]+' | cut -d '"' -f3) ; do \
curl "http://10.3.3.60:5000/group/$uid" -H 'Content-Type: application/json' -d '{"t":"group-'$(rndstr)'","d":"G-'$(rndstr)'","x":"customdata '$(rndstr)'","tg":["tagG1","tagG2"]}' ; done

#UPDATE
for pid in $(curl "http://10.3.3.60:5000/projects/0x2" 2>/dev/null | grep -oE 'uid":"0x[0-9]+' | cut -d '"' -f3) ; do \
for gid in $(curl "http://10.3.3.60:5000/groups/$pid" 2>/dev/null | grep -oE 'uid":"0x[0-9]+' | cut -d '"' -f3) ; do \
curl "http://10.3.3.60:5000/subgroup/$gid" -H 'Content-Type: application/json' -d '{"t":"subgroup-'$(rndstr)'","d":"SG-'$(rndstr)'","x":"customdata '$(rndstr)'","tg":["tagSG1","tagSG2"]}' ; done ; done

#GET
for pid in $(curl "http://10.3.3.60:5000/projects/0x2" 2>/dev/null | grep -oE 'uid":"0x[0-9]+' | cut -d '"' -f3) ; do \
for gid in $(curl "http://10.3.3.60:5000/groups/$pid" 2>/dev/null | grep -oE 'uid":"0x[0-9]+' | cut -d '"' -f3) ; do \
for sgid in $(curl "http://10.3.3.60:5000/subgroups/$gid" 2>/dev/null | grep -oE 'uid":"0x[0-9]+' | cut -d '"' -f3) ; do \
curl "http://10.3.3.60:5000/item/$sgid" -H 'Content-Type: application/json' -d '{"t":"item-'$(rndstr)'","d":"I-'$(rndstr)'","x":"customdata '$(rndstr)'","tg":["tagI1","tagI2"]}' ; done ; done ; done

#GETALL

curl "http://10.3.3.60:5000/projecttree/0x2"