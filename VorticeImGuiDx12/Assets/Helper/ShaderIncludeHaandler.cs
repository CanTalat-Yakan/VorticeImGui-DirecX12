﻿using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using SharpGen.Runtime;
using Vortice.Dxc;

namespace Engine.Helper;

public sealed class ShaderIncludeHandler : CallbackBase, IDxcIncludeHandler
{
    private readonly Dictionary<string, SourceCodeBlob> _sourceFiles = new();
    private readonly string[] _includeDirectories;

    public ShaderIncludeHandler(params string[] includeDirectories)
    {
        _includeDirectories = includeDirectories;
    }

    protected override void DisposeCore(bool disposing)
    {
        foreach (var pinnedObject in _sourceFiles.Values)
            pinnedObject?.Dispose();

        _sourceFiles.Clear();
    }

    public Result LoadSource(string fileName, out IDxcBlob includeSource)
    {
        if (fileName.StartsWith("./"))
            fileName = fileName.Substring(2);

        var includeFile = GetFilePath(fileName);

        if (string.IsNullOrEmpty(includeFile))
        {
            includeSource = default;

            return Result.Fail;
        }

        if (!_sourceFiles.TryGetValue(includeFile, out SourceCodeBlob sourceCodeBlob))
        {
            byte[] data = NewMethod(includeFile);

            sourceCodeBlob = new SourceCodeBlob(data);
            _sourceFiles.Add(includeFile, sourceCodeBlob);
        }

        includeSource = sourceCodeBlob.Blob;

        return Result.Ok;
    }

    private static byte[] NewMethod(string includeFile) => File.ReadAllBytes(includeFile);

    private string GetFilePath(string fileName)
    {
        for (int i = 0; i < _includeDirectories.Length; i++)
        {
            var filePath = Path.GetFullPath(Path.Combine(_includeDirectories[i], fileName));

            if (File.Exists(filePath))
                return filePath;
        }

        return null;
    }

    private sealed class SourceCodeBlob : IDisposable
    {
        internal IDxcBlob Blob => _blob;

        private GCHandle _dataPointer;
        private IDxcBlobEncoding _blob;

        public SourceCodeBlob(byte[] data)
        {
            _dataPointer = GCHandle.Alloc(data, GCHandleType.Pinned);

            _blob = DxcCompiler.Utils.CreateBlob(_dataPointer.AddrOfPinnedObject(), data.Length, Dxc.DXC_CP_UTF8);
        }

        public void Dispose()
        {
            //_blob?.Dispose();
            _blob = null;

            if (_dataPointer.IsAllocated)
                _dataPointer.Free();
            _dataPointer = default;
        }
    }
}