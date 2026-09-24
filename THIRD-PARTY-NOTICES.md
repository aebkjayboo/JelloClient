# Third-party notices

Jello Client bundles the following third-party components.

## Poppins

Files: `Assets/Fonts/Poppins-Regular.ttf`, `Assets/Fonts/Poppins-Medium.ttf`, `Assets/Fonts/Poppins-SemiBold.ttf`, `Assets/Fonts/Poppins-Bold.ttf`

Copyright 2020 The Poppins Project Authors (https://github.com/itfoundry/Poppins)

Licensed under the SIL Open Font License, Version 1.1. The full licence text is
distributed with the fonts in `Assets/Fonts/OFL.txt` and is reproduced below.

    Copyright 2020 The Poppins Project Authors (https://github.com/itfoundry/Poppins)
    
    This Font Software is licensed under the SIL Open Font License, Version 1.1.
    This license is copied below, and is also available with a FAQ at:
    http://scripts.sil.org/OFL
    
    
    -----------------------------------------------------------
    SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007
    -----------------------------------------------------------
    
    PREAMBLE
    The goals of the Open Font License (OFL) are to stimulate worldwide
    development of collaborative font projects, to support the font creation
    efforts of academic and linguistic communities, and to provide a free and
    open framework in which fonts may be shared and improved in partnership
    with others.
    
    The OFL allows the licensed fonts to be used, studied, modified and
    redistributed freely as long as they are not sold by themselves. The
    fonts, including any derivative works, can be bundled, embedded, 
    redistributed and/or sold with any software provided that any reserved
    names are not used by derivative works. The fonts and derivatives,
    however, cannot be released under any other type of license. The
    requirement for fonts to remain under this license does not apply
    to any document created using the fonts or their derivatives.
    
    DEFINITIONS
    "Font Software" refers to the set of files released by the Copyright
    Holder(s) under this license and clearly marked as such. This may
    include source files, build scripts and documentation.
    
    "Reserved Font Name" refers to any names specified as such after the
    copyright statement(s).
    
    "Original Version" refers to the collection of Font Software components as
    distributed by the Copyright Holder(s).
    
    "Modified Version" refers to any derivative made by adding to, deleting,
    or substituting -- in part or in whole -- any of the components of the
    Original Version, by changing formats or by porting the Font Software to a
    new environment.
    
    "Author" refers to any designer, engineer, programmer, technical
    writer or other person who contributed to the Font Software.
    
    PERMISSION & CONDITIONS
    Permission is hereby granted, free of charge, to any person obtaining
    a copy of the Font Software, to use, study, copy, merge, embed, modify,
    redistribute, and sell modified and unmodified copies of the Font
    Software, subject to the following conditions:
    
    1) Neither the Font Software nor any of its individual components,
    in Original or Modified Versions, may be sold by itself.
    
    2) Original or Modified Versions of the Font Software may be bundled,
    redistributed and/or sold with any software, provided that each copy
    contains the above copyright notice and this license. These can be
    included either as stand-alone text files, human-readable headers or
    in the appropriate machine-readable metadata fields within text or
    binary files as long as those fields can be easily viewed by the user.
    
    3) No Modified Version of the Font Software may use the Reserved Font
    Name(s) unless explicit written permission is granted by the corresponding
    Copyright Holder. This restriction only applies to the primary font name as
    presented to the users.
    
    4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
    Software shall not be used to promote, endorse or advertise any
    Modified Version, except to acknowledge the contribution(s) of the
    Copyright Holder(s) and the Author(s) or with their explicit written
    permission.
    
    5) The Font Software, modified or unmodified, in part or in whole,
    must be distributed entirely under this license, and must not be
    distributed under any other license. The requirement for fonts to
    remain under this license does not apply to any document created
    using the Font Software.
    
    TERMINATION
    This license becomes null and void if any of the above conditions are
    not met.
    
    DISCLAIMER
    THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
    EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF
    MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
    OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
    COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
    INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
    DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
    FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM
    OTHER DEALINGS IN THE FONT SOFTWARE.

## Bloxstrap

Files: `Assets/Mods/OldAvatarBackground.rbxl`, `Assets/Mods/Sounds/*.mp3`,
`Assets/Mods/Cursor/From2006/*.png`, `Assets/Mods/Cursor/From2013/*.png`

These modification preset assets are taken from Bloxstrap
(https://github.com/pizzaboxer/bloxstrap), where they ship as embedded resources
under `Bloxstrap/Resources/Mods/`. Jello embeds the same files and applies them
the same way. Bloxstrap is MIT licensed:

    MIT License

    Copyright (c) 2022 pizzaboxer

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.

### Provenance of these particular files

The MIT licence above covers Bloxstrap's own work. It does not, and cannot,
relicense the contents of these specific binaries, which did not originate with
Bloxstrap:

- `Sounds/OldWalk.mp3`, `Sounds/OldJump.mp3`, `Sounds/OldGetUp.mp3` are the
  original pre-2015 Roblox character sound effects.
- `Cursor/From2006/*.png` and `Cursor/From2013/*.png` are the original Roblox
  mouse cursor textures from those eras.
- `OldAvatarBackground.rbxl` is a Roblox place file for the old avatar editor
  backdrop.

All of the above are assets of Roblox Corporation, redistributed here for
interoperability with the Roblox client in the same manner as Bloxstrap. They
are not covered by Bloxstrap's MIT licence, nor by any licence Jello can grant.
`Sounds/Empty.mp3` is a silent placeholder and carries no such claim.

Any of these can be overridden by dropping a replacement of the same name into
`%LOCALAPPDATA%\JelloClient\ModAssets\`.
