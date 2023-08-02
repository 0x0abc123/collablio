#/bin/bash
# if /tmp/prevent_dgraph_start exists then we don't run the dgraph docker standalone until it is deleted
while true
do
if [[ -f "/tmp/prevent_dgraph_start" ]] ; then sleep 10 ; continue ; fi
docker run --rm -it -p 127.0.0.1:8080:8080 -p 127.0.0.1:9080:9080 -p 127.0.0.1:8000:8000 -v /dgraph:/dgraph dgraph/standalone:v21.12.0
done