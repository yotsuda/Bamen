# Bamen

Write the shots as text, and watch a moving storyboard beside the source.
Bamen (場面, "scene") is to the camera what [Shosa](https://github.com/yotsuda/Shosa) is to movement and
[Lily#](https://yotsuda.github.io/LilySharp/) is to music: a small language you can read, diff and mend, which
compiles to an animatic, a storyboard sheet and files other tools load.

```
dance "dance.shosa"                  // the dancers, their movement and the song

shot 1 wide   eye  bars 1-4  look alice bob carol
shot 2 medium low  bars 5-6  look alice
shot 3 close       bars 7-8  look alice.face
```

The camera is written in the words of film, not in coordinates: the compiler places it from the figures'
joints and the set, and checks the cut against the grammar of film (the 180-degree line, jump cuts, a subject
out of frame or hidden behind a wall).

## What it is for

- **The control layer AI video lacks.** The same figures and the same set in every shot, a camera you can
  name, and one shot changed while the rest stay. The frames can go to a video model as guides.
- **Music videos and dance videos** from Lily# songs and Shosa choreography.
- **Cutscenes** for small game teams, exported with the camera.
- **Agents.** The whole language is one text an agent reads; the compiler checks what it wrote and draws it.

It does not render the final picture, model in 3D, or make movement (Shosa and other sources do).

## Status

A prototype (M0): fixed cameras, `bamen check`, `bamen cameras` (JSON), `bamen sheet` (PNG), and a VS Code
extension (`editors/vscode`, not published) whose preview plays the dance cut by cut and writes an MP4 with
the song. The design and the plan are in Japanese: `docs/DESIGN.md`, `docs/ROADMAP.md`.

## License

MIT (`LICENSE`).
