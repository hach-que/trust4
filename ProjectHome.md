A trust-based distribution example for .p2p

## Requirements ##
  * If you're on Linux, you'll need **[Mono 2.8](http://www.go-mono.com/mono-downloads/download.html)**.
  * If you're on Windows, you need **[.NET Framework 3.5 SP1](http://www.microsoft.com/downloads/en/details.aspx?FamilyID=ab99342f-5d1a-413d-8319-81da479ab0d7&displaylang=en)**.

## Running ##
Before you can run the application, you have to do a few things:
  * Generate 4 GUIDs for yourself at [this site](http://www.guidgenerator.com/online-guid-generator.aspx).  Place them in the settings.txt file as instructed.
  * Set your _public_ IP address in the settings.txt file.  ~~If you're behind dynamic IP set it to "dynamic" (without quotes)~~ _You must set your IP address manually as the dynamic setting is broken at this time._
  * Make sure you forward your peer-to-peer port if you're behind a NAT.
  * Make sure that your first DNS server in your network settings is set to 127.0.0.1.
  * You should be all good to go.

## Mappings / Domain Registration ##
Take a look at the mappings.txt file if you want to add your own domains to the network.

## Peers ##
Unlike previous versions, your peers.txt file now comes with a single trust cache (Trust Cache 01; it is highly recommended you add the other trust caches listed below).

### Additional Peers ###
```
# Trust Cache 02
0.5 173.193.221.29 12000 99743a87-7d31-4407-8cb4-1443fcaa0b97 0362060b-a64e-483a-b53f-ff9502ca9673 028e7318-2fce-46d1-af74-5a47c5456042 21322e40-40e8-4b1a-b0c0-0977fe885501
```