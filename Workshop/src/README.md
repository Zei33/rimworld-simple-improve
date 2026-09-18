# Workshop source

Edit the files here. The nine `../<Language>.md` files are **generated** and are
overwritten by the builder.

    body/<Language>.bbcode   the mod information block, which is the only part of
                             the store page a human writes. It opens with
                             [h1]<name>[/h1] and stops before the first separator;
                             the footer, the support block and the list of other
                             mods are all composed.
    mod.json                 the localised item title, the mod's own name, and the
                             one-line blurb that appears on every *other* mod's
                             page in that language.

Rebuild from the workspace root, which is the directory holding both this repo
and `workshop-content-builder/`:

    python3 workshop-content-builder/wcb.py build

No absolute path on purpose. This file ships in a public repository, so it must
not carry anyone's home directory.

Do not add a tenth `.md` file to `../`. `Tests/MaterialCostFieldTests.cs` globs
`Workshop/*.md` non-recursively and asserts the count is exactly ten with
`About/About.xml`. A subdirectory such as this one is invisible to that glob,
which is why the sources live here.
