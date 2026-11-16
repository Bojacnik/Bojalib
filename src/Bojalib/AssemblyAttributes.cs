using System.Runtime.CompilerServices;

// Don't zero stackalloc'd buffers and locals: every buffer in this library is fully written before it's read.
[module: SkipLocalsInit]
