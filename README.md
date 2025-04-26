# CodingExample
These two scripts are the core of the movement in my game Train Reaction. They are a good example of how I tend to code since a lot of the game relies on these scripts.

The car script is the base for train movement. It is mostly straight forward and only handles cars moving along track and storing cargo inside it. This script has a good example of when I return to a script and rewrite functionality. All of the "Limbo" stuff is a system i added in retrospect in order to smooth out the trains' turning. Limbo only takes over the original functionality at certain times in a way that enhances the system instead of conflicting with it.

The engine script handles all of the train logic when it comes to what movement the train should take. It searches the track it is traveling down to find signals that it must obey such as red lights to stop at. This script is a good example at how I handle generic game programming concepts.
