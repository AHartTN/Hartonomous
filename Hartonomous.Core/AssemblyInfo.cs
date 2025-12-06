// Copyright (c) 2025 Anthony Hart. All Rights Reserved.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Hartonomous.Data.IntegrationTests")]
[assembly: InternalsVisibleTo("Hartonomous.Data.Tests")]
[assembly: InternalsVisibleTo("Hartonomous.Core.Tests")]

namespace Hartonomous.Core;

/// <summary>
/// Global assembly information and constants
/// </summary>
public static class AssemblyInfo
{
    public const string Copyright = "Copyright � 2025 Anthony Hart. All Rights Reserved.";
    public const string Company = "Hart Industries";
    public const string Product = "Hartonomous Atomic Content-Addressable Storage";
}
