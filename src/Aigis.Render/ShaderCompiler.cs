using System.Runtime.InteropServices;
using Silk.NET.Shaderc;

namespace Aigis.Render;

/// <summary>
/// Compiles GLSL to SPIR-V at runtime via shaderc (native lib bundled — no Vulkan SDK
/// needed). Runtime compilation is what lets agents author/hot-reload shaders later;
/// stable shaders can move to build-time SPIR-V without changing callers. Public (not
/// internal) so <c>PrecompiledShadersSyncTests</c> can recompile <see cref="ShaderSource"/>
/// and assert it still matches <see cref="PrecompiledShaders"/> byte-for-byte — the
/// regression net for "someone edited the GLSL but forgot to regenerate the SPIR-V".
/// </summary>
public sealed unsafe class ShaderCompiler : IDisposable
{
    private readonly Shaderc _sc;
    private readonly Compiler* _compiler;

    public ShaderCompiler()
    {
        _sc = Shaderc.GetApi();
        _compiler = _sc.CompilerInitialize();
    }

    public uint[] Compile(string source, ShaderKind kind, string name)
    {
        var options = _sc.CompileOptionsInitialize();
        try
        {
            var result = _sc.CompileIntoSpv(_compiler, source, (nuint)source.Length, kind, name, "main", options);
            try
            {
                var status = _sc.ResultGetCompilationStatus(result);
                if (status != CompilationStatus.Success)
                {
                    var message = _sc.ResultGetErrorMessageS(result);
                    throw new InvalidOperationException($"Shader compile failed ({name}): {status}: {message}");
                }

                var byteLength = (int)_sc.ResultGetLength(result);
                var bytes = _sc.ResultGetBytes(result);
                var words = new uint[byteLength / 4];
                new Span<byte>(bytes, byteLength).CopyTo(MemoryMarshal.AsBytes(words.AsSpan()));
                return words;
            }
            finally
            {
                _sc.ResultRelease(result);
            }
        }
        finally
        {
            _sc.CompileOptionsRelease(options);
        }
    }

    public void Dispose()
    {
        _sc.CompilerRelease(_compiler);
        _sc.Dispose();
    }
}
