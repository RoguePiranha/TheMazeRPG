# Runtime Sprite Catalogs

The runtime catalogs contain every curated bitmap that ships with the game. The larger
`Assets/Sprites/Pixel Crawler - *` directories are source libraries and are not bundled; an asset
enters the runtime catalog only after it is copied into a curated folder.

## Actor sets

`sprites.json` groups actors by their original Pixel Crawler set. Each set declares:

- `sourcePack`: source-art provenance.
- `placement`: `actor`.
- `facing`: `screen-south`; the orthographic camera never rotates or mirrors actors.
- `anchor`: `bottom-center`; transparent frame padding is normalized at load time.
- `animation` and `frame`: the currently selected strip and frame.
- `sprites`: gameplay lookup key to curated asset path.

Lookup remains most-specific first: hero class; enemy race and class; enemy race; enemy class.

## Dungeon tile sets

`terrain.json` groups atlas sprites by dungeon theme. Every theme currently defines:

| Sprite ID | Placement | Facing | Layer | Walkable |
| --- | --- | --- | --- | --- |
| `floor.room` | room floor | none | ground | yes |
| `floor.corridor` | corridor floor | none | ground | yes |
| `doorway.east-west` | doorway | east-west passage | ground | yes |
| `doorway.north-south` | doorway | north-south passage | ground | yes |
| `wall.fill` | wall | none | structure | no |

`sourceX` and `sourceY` are atlas pixels. `columns` and `rows` describe an authored pattern of
`gridSize` cells. World coordinates select the corresponding cell without rotating or mirroring
the source art.

Wall exposure and doorway orientation are derived from neighboring walkable cells. Directional
threshold lines are code-rendered overlays; decorations, hazards, and theme landmarks are also
procedural and therefore are not falsely listed as bitmap sprites.
