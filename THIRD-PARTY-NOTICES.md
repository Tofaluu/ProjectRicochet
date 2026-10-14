# Third-party notices

This project includes the following third-party components. They are not covered by this project's
"all rights reserved" notice; each keeps its own license.

## Microsoft ASP.NET Core SignalR client and dependencies

The DLLs in `UnityClient/Assets/Plugins/SignalR/` (Microsoft.AspNetCore.SignalR.Client 8.0.0 and the
packages it depends on: Microsoft.AspNetCore.*, Microsoft.Extensions.*, Microsoft.Bcl.*, System.*),
from https://github.com/dotnet/aspnetcore and https://github.com/dotnet/runtime, and the JavaScript
client `UnityClient/Assets/WebGLTemplates/Ricochet/signalr.min.js` (@microsoft/signalr 8.0.7, used by the
browser build), from https://github.com/dotnet/aspnetcore.

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

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
```

## Json.NET (Newtonsoft.Json)

Used by the browser build through Unity's package `com.unity.nuget.newtonsoft-json`
(https://github.com/JamesNK/Newtonsoft.Json).

```
The MIT License (MIT)

Copyright (c) 2007 James Newton-King

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## Nunito font

`UnityClient/Assets/Fonts/Nunito-*.ttf` - Copyright 2014 The Nunito Project Authors
(https://github.com/googlefonts/nunito), licensed under the SIL Open Font License 1.1; the full licence
is in `UnityClient/Assets/Fonts/OFL.txt`. The fonts may be bundled and sold with software; they may not be
sold on their own.

## Unity

The client is made with Unity 6 and its packages, under the Unity Terms of Service and the
licenses of the individual packages (https://unity.com/legal).
