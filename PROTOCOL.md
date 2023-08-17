# Protocol
A brief guide to the protocol Dynknock follows on both the client and server.<br/>
Not a full guide, but one with all the necessary bits, such that you could probably write your own implementation off of just this, and to serve as a reminder in case I break something vital.

### Sequence Generation
- First, get the key from the config. If the key is a valid Base64 string, the key is the represented bytes. Otherwise, it is the unicode representation of that text.
- We then calculate the period, which is `unixTime / interval` using integer division.
- For each port desired, we use HMACSHA256, with the key being the key previously fetched, and the content being `currentPeriod + portIndex + sequenceLength` using string concatenation, where `currentPeriod` is the previously calculated period, `portIndex` is the index of this port in the sequence, and `sequenceLength` is the total length of the sequence.
- From the generated hash, the first 4 bytes are read as an unsigned integer. `(generatedInt % 65535) + 1` represents the port. The last bit of the 5th byte of the hash indicates whether UDP or TCP should be used, with a 0 indicating a TCP SYN packet should be used, and a 1 indicating UDP.

### Doorbell
- Before beginning the knock sequence, the "doorbell" must be rung. It is a statically defined port, who's packet must be UDP and contain the word "DOORBELL" encoded in UTF8 at the beginning of the payload. Immediately proceeding that is the UTF8 encoded integer string representing the current period.

### Knocking
- The knocking system is fairly standard. After ringing the doorbell, you are expected to continue the knock sequence, by simply sending a packet with an empty payload to the correct port with the correct protocol.  If you get them all correct, the designated command is run.

