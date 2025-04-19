# nsnipes
My 'interpretation' of the classic NSnipes game 

To be clear, I loved the original game that came with Novell Netware, and spent many an hour battling with colleagues across the network.  This project does not intend to be faithful to the original, the map will be different, the game play will be different (but similar);  but, what I hope to achieve with this is the ability to play this game with other players across the internet and bring back some of the nostalgia and FUN that the original had.
So this will be a 'stylised' version.  It's also 'my' version because I'm not trying to rip anyone off, or profit by copying.

This is an exercise in programming and networking as much as it is a journey into my distant memories.

This project is built with the following dependencies:
- https://github.com/gui-cs/Terminal.Gui


I used the following https://stackoverflow.com/questions/56918471/how-can-i-increase-corridor-width-in-a-maze Python code to generate the maze at an appropriate scale.  I captured the output produced by the following Python code, and used a Text editor to change lines, make the maze wrap around and break through some walls to simplify.

```
import random


def make_maze(w = 16, h = 8, scale=0):

    h0, h1, h2, h3 = "+--", "+  ", "|  ", "   "
    h0 += scale * '----'
    h1 += scale * '    '
    h2 += scale * '    '
    h3 += scale * '    '
    vis = [[0] * w + [1] for _ in range(h)] + [[1] * (w + 1)]
    ver = [[h2] * w + ['|'] for _ in range(h)] + [[]]
    hor = [[h0] * w + ['+'] for _ in range(h + 1)]

    def walk(x, y):
        vis[y][x] = 1

        d = [(x - 1, y), (x, y + 1), (x + 1, y), (x, y - 1)]
        random.shuffle(d)
        for (xx, yy) in d:
            if vis[yy][xx]: continue
            if xx == x: hor[max(y, yy)][x] = h1
            if yy == y: ver[y][max(x, xx)] = h3
            walk(xx, yy)

    walk(random.randrange(w), random.randrange(h))

    s = ""
    for (a, b) in zip(hor, ver):
        s += ''.join(a + ['\n'] + b + ['\n'])
        for _ in range(scale):
            s += ''.join(b + ['\n'])
    return s



print(make_maze(scale=0))
print('\n\n')
print(make_maze(scale=1))
print('\n\n')
print(make_maze(scale=2))
print('\n\n')
print(make_maze(scale=3))
print('\n\n')
```
Full Credit for this amazing scaleable Maze generator goes to https://stackoverflow.com/users/2875563/reblochon-masque



So resources that I took a look at:
https://en.wikipedia.org/wiki/Snipes_(video_game)

https://www.youtube.com/watch?v=IXsJhoW0C78
https://www.youtube.com/watch?v=1iGKsuZlIIo
https://www.youtube.com/watch?v=85IcFHTsVQs


https://www.networkworld.com/article/830595/infrastructure-management-novell-and-the-computer-game-that-changed-networking.html

https://medium.com/venture-evolved/snipes-the-game-that-gave-birth-to-lans-e9dc169873e4

https://playclassic.games/games/arcade-dos-games-online/play-snipes-online/play/

https://www.giantbomb.com/snipes/3030-12025/

https://github.com/Davidebyzero/Snipes
