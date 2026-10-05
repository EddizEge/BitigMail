# Third-Party Software Notices and Information

This project incorporates third-party software components subject to the following terms and notices:

---

## MimeKit 4.17.0

- **Project URL:** https://github.com/jstedfast/MimeKit
- **NuGet:** https://www.nuget.org/packages/MimeKit/4.17.0
- **License:** MIT License
- **Copyright:** Copyright (C) 2012-2024 Jeffrey Stedfast <jestedma@microsoft.com>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

---

## MailKit 4.17.0

- **Project URL:** https://github.com/jstedfast/MailKit
- **NuGet:** https://www.nuget.org/packages/MailKit/4.17.0
- **License:** MIT License
- **Copyright:** Copyright (C) 2013-2024 Jeffrey Stedfast <jestedma@microsoft.com>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

---

## Frontend test-only dependencies

The following exact development-only packages are used by the frontend test suite and are not production runtime dependencies:

- **@testing-library/react 16.3.0** — MIT License — https://github.com/testing-library/react-testing-library
- **@testing-library/dom 10.4.1** — MIT License — https://github.com/testing-library/dom-testing-library
- **jsdom 27.0.0** — MIT License — https://github.com/jsdom/jsdom

Their license texts and transitive notices are retained in the installed package metadata and must accompany redistribution where applicable. An `npm audit --audit-level=low` run after installation reported 0 vulnerabilities.

---

## Aspose.Email for .NET 24.8.0

- **Project URL:** https://products.aspose.com/email/net/
- **NuGet:** https://www.nuget.org/packages/Aspose.Email/24.8.0
- **License:** Commercial Proprietary (Aspose Pty Ltd), used in Evaluation Mode under standard vendor terms.
- **Copyright:** Copyright 2002-2024 Aspose Pty Ltd.

### Isolated diagnostic version

`lab/stage4-olm-next` pins Aspose.Email26.7.0 from https://www.nuget.org/packages/Aspose.Email/26.7.0 for a read-only comparison on a public OLM fixture. It is an evaluation-only laboratory dependency under the vendor's commercial terms, not a production Engine upgrade or a redistribution license. The production Engine remains24.8.0. Vendor package license/notices remain in package metadata. The MIT license of `fixtures/vendor-olm` applies to the example corpus, not this SDK.

## System.Formats.Asn1 8.0.1

- **Publisher:** Microsoft / .NET Foundation and Contributors.
- **NuGet:** https://www.nuget.org/packages/System.Formats.Asn1/8.0.1
- **License:** MIT. Package LICENSE.TXT and THIRD-PARTY-NOTICES.TXT must accompany redistribution as applicable.
- **Version decision:** exact 8.0.1 override replaces the vulnerable transitive 7.0.0 package; Microsoft advisory https://github.com/advisories/GHSA-447r-wph3-92pm identifies 8.0.1 as patched. Aspose.Email remains 24.8.0.

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

---

## Google.Apis.Auth 1.76.0

- **Project URL:** https://github.com/googleapis/google-api-dotnet-client
- **NuGet:** https://www.nuget.org/packages/Google.Apis.Auth/1.76.0
- **License:** Apache License 2.0
- **Copyright:** Copyright Google LLC.

Licensed under the Apache License, Version 2.0. You may obtain a copy at
https://www.apache.org/licenses/LICENSE-2.0. Distributed on an "AS IS" basis,
without warranties or conditions of any kind.

---

## Microsoft.Identity.Client 4.89.0

- **Project URL:** https://github.com/AzureAD/microsoft-authentication-library-for-dotnet
- **NuGet:** https://www.nuget.org/packages/Microsoft.Identity.Client/4.89.0
- **License:** MIT License
- **Copyright:** Copyright (c) Microsoft Corporation. All rights reserved.

The MIT License (MIT)

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
