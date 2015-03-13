# Steps to follow #

First make sure that you note down at what point it starts working.  You should make each of the changes below and then test after each one.  Do **not** test with your web browser until you have tried with your local DNS client (such as dig or nslookup) first.

You should always attach the output of your Trust4 server in any bug reports you file.

### 1. Ensure that files are formatted correctly ###
  * Set the IP address configuration in settings.txt to match that displayed at http://www.whatsmyip.org/.
  * Make sure that you don't have commas or anything like that seperating GUIDs in settings.txt.  We use tabs and spaces here.
  * In your peers.txt file, try it with only a single space between each field.
  * In your peers.txt file, try it with a trust level of 0.  Some systems are unable to parse the float (_could this be a locale issue?_)
  * Comment out mappings.txt if you uncommented any lines (i.e. disable all domains that your Trust4 server is serving).

If your Trust4 server is still crashing before bootstrapping at this stage, file a bug report detailing your locale and any other environment specific information, as well as a copy of settings, peers and mappings.txt files.  You can exclude mappings.txt if it is fully commented out.

### 2. Ensure that your network settings are correct ###
  * Make sure that your UDP port 12000 (or whatever peerport is set to in settings.txt) is forwarded on any NAT or routing systems you are behind to your local IP address.
  * Ensure that the only DNS server configured in your DNS settings is 127.0.0.1.  This mode should only be used for testing as without another DNS server configure you won't be able to reach internet domains.  Make sure you set it back when you are done.
  * Clear your DNS cache.
  * Try to issue DNS requests with either nslookup or dig.  The instructions / inputs for each tool are (# indicates command line shell or cmd.exe, > indicates prompts in application):
> _nslookup_
```
  # nslookup
  > server 127.0.0.1
  > lserver 127.0.0.1
  > redpoint.p2p
  .. results should show here ..
```

> _dig_
```
  # dig @127.0.0.1 -p 53 redpoint.p2p
  .. results should show here ..
```

> Since the domain is registered by the first trust cache, you should get at least one CNAME records shown.
  * If you get no CNAME records shown, check the output of the Trust4 server to see if it timed out before the node responds  (signified by "Node timed out before response" showing in the output or something to that effect).  Double check step 1 (that your NAT is forwarded) and if it is configured correctly, then you are too far away from a trust cache.  The total time allowed for a request is 1.5 seconds and if you are positioned too far away from the first (and only) trust cache then you won't be able to resolve .p2p addresses.  This issue will be significantly reduced as more peers and trust caches are brought online.

### 3. File a bug report ###
If you have tried all of the steps above and they have failed to allow you to lookup domains through nslookup or dig, you should [file a bug report](http://code.google.com/p/trust4/issues/entry?template=Unable%20to%20resolve%20domains) with the following information attached:
  * A full copy of the output produced by the Trust4 server.
  * A full copy of the output produced by nslookup or dig.
  * A list of the domain you tried.
  * Your current operating system, environment variables (such as whether you are running it as an administrator, your locale, etc.)
  * The settings.txt file.
  * The peers.txt file.
  * The mappings.txt file (only if it is not all commented out).
  * Your current framework version (such as .NET 3.5 SP1 or Mono 2.8.1).
  * Any other details you think might be relevant in finding out the problem.

There is a template in the bug tracker for **"Unable to resolve domains"**.  You **must** select this template when filing your bug report, or your report may be _ignored or closed_.