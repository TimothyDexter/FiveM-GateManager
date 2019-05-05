# GateManager
Control gates in FiveM.  Lockable gate states but retain natural movement.  Gates can be controlled mid-open/close to change their direction.



Disclaimer:

To utilize these file(s) you will need to strip project methods (mostly logging) and compile a .dll to use a resource on your server. Otherwise, you are best served using the source code to derive your own implementation in the language of your choice.

To use as standalone .dll:

    Add new gate locations where applicable.

This script synchronizes clients by interpolating the movement to maintain the natural animation movement for all clients.
