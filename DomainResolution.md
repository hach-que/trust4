# Introduction #

The following terms are used below:
  * DNS client - This is often your operating system on behalf of your web browser.
  * P2P client - The peer-to-peer component of Trust4.
  * DNS server - The DNS server component of Trust4 which serves answers to your DNS client.

## Resolution ##
  * Your DNS client asks the DNS server for a domain such as 'wikileaks.p2p'.
  * The P2P client asks your immediate peers for the answer to 'wikileaks.p2p'.  At most, it waits 1.5 seconds due to the limited timeout that DNS clients have.
  * It determines the correct 'wikileaks.p2p' -> 'wikileaks.p2p.hash.key' based on the trust order of your peers.  This is the only stage whereby records can not be verified for integrity.
  * The DNS server returns the CNAME record.  It is important that it returns the CNAME record and does not attempt to do the record lookup for 'wikileaks.p2p.hash.key' transparently, due to the fact that except in very limited circumstances, the DNS client would timeout before it managed to return the results for the CNAME record.
  * The DNS client realizes it has a CNAME record, and re-requests the answers for 'wikileaks.p2p.hash.key'.
  * The P2P client asks your immediate peers for the answer to 'wikileaks.p2p.hash.key'.
  * This time around, the P2P client has a 'hash', which is the hash of the public key that is transferred along with the records.
  * The P2P client verifies that the public key in the records is correct, and then verifies that the record data is signed correctly using the full public key and the signature that was transferred along with the record data.
  * If the record data is intact, the DNS server returns the records to the DNS client.