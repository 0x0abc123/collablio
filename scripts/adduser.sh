#!/bin/bash
if [ ! "$(which xxd)" ]
then
    echo xxd is not installed, please install with \"apt install xxd\" and re-run this tool
fi

echo -n "Enter Username: "
read USERNAME
while true
do
	TMPFILE="/tmp/$(head -c 20 /dev/urandom | base64 | tr -d '/+=')"
	grub-mkpasswd-pbkdf2 -c 10000 -s 16 -l 32 | tee "$TMPFILE"
	HASH="10000"
	for f in $(cat "$TMPFILE" | grep 'grub.pbkdf2' | awk -F '.' '{ print $5 " " $6 }')
	do
	  B64ENC=$(echo $f | xxd -r -p | base64)
	  HASH="$HASH.$B64ENC"
	done
	shred -u "$TMPFILE"
	if [ "$HASH" != "10000" ]
	then
		break
	fi
done

curl -H "Content-Type: application/json" -X POST localhost:8080/mutate?commitNow=true -d "{\"query\":\"{ q(func: eq(username, \\\"$USERNAME\\\")) {v as uid} }\",\"set\":{\"uid\":\"uid(v)\",\"dgraph.type\":\"U\",\"username\":\"$USERNAME\",\"password\":\"$HASH\"}}"
