//-----------------------------------------------------------------------------
// Copyright 2024 Igor Spichkin
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//-----------------------------------------------------------------------------

using System.IO;
using JetBrains.Annotations;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Server.ModularComputer.Devices;

public sealed class VirtualDisksManager
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    [Dependency] private readonly IResourceManager _resource = default!;

    [PublicAPI] public readonly string DiskExtension;

    [PublicAPI] public readonly string RootFolderName;

    [PublicAPI] public readonly ISawmill? Sawmill;

    public VirtualDisksManager(string rootFolderName, string diskExtension, ISawmill? sawmill)
    {
        IoCManager.InjectDependencies(this);

        RootFolderName = rootFolderName;
        DiskExtension = diskExtension;
        Sawmill = sawmill;

        DeleteAll();
        CreateRoot();
    }

    [PublicAPI]
    public string GetRootPath()
    {
        var forkId = _cfg.GetCVar(CVars.BuildForkId);

        if (string.IsNullOrEmpty(forkId))
            forkId = "ss14";

        return $"{Path.GetTempPath()}{forkId}_{RootFolderName}/";
    }

    [PublicAPI]
    public string GetRandomFilePath()
    {
        return $"{GetRootPath()}{Path.GetRandomFileName()}.{DiskExtension}";
    }

    [PublicAPI]
    public void CreateRoot()
    {
        var directory = GetRootPath();

        if (Directory.Exists(directory))
            return;

        Sawmill?.Info($"Creating a virtual disks root `{directory}`");
        Directory.CreateDirectory(directory);
    }

    [PublicAPI]
    public void DeleteAll()
    {
        var directory = GetRootPath();

        Sawmill?.Info($"Deleting a virtual disks root `{directory}`");

        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    [PublicAPI]
    public void DeleteDisk(VirtualDisk disk)
    {
        if (!File.Exists(disk.Path))
            return;

        Sawmill?.Debug($"Deleting a virtual disk file '{disk.Path}'");
        File.Delete(disk.Path);
    }

    [PublicAPI]
    public VirtualDisk CreateDisk(int size)
    {
        DebugTools.Assert(size > 0);

        var disk = new VirtualDisk(GetRandomFilePath(), size);
        Sawmill?.Debug($"Created a virtual disk file '{disk.Path}'");

        return disk;
    }

    [PublicAPI]
    public VirtualDisk CreateDiskFromCopy(ResPath copy, int? size)
    {
        var ok = false;
        var newPath = GetRandomFilePath();

        foreach (var root in _resource.GetContentRoots())
        {
            var testPath = Path.GetFullPath(copy.ToRelativeSystemPath(), root.CanonPath);

            if (!Path.Exists(testPath))
                continue;

            Sawmill?.Debug($"Copying pre-existed virtual disk file {testPath} to {newPath}");
            File.Copy(testPath, newPath);
            ok = true;
        }

        if (!ok)
            throw new InvalidOperationException($"File {copy} not found!");

        if (size is null)
        {
            using var f = File.OpenRead(newPath);
            size = (int?)f.Length;
        }

        DebugTools.Assert(size > 0);

        return new VirtualDisk(newPath, size.Value);
    }

    [PublicAPI]
    public VirtualDisk CreateDiskFromStream(Stream stream)
    {
        var size = (int)stream.Length;
        var filePath = GetRandomFilePath();
        var content = new byte[stream.Length];

        stream.ReadToEnd(content);

        File.WriteAllBytes(filePath, content);

        Sawmill?.Debug($"Created a virtual disk file from stream '{filePath}'");

        return new VirtualDisk(filePath, size);
    }

    [PublicAPI]
    public ResPath TryDumpDisk(VirtualDisk disk, string folderPrefix = "virtual_disks")
    {
        var content = File.ReadAllBytes(disk.Path);
        var fileName = Path.GetFileName(disk.Path);
        var resPath = new ResPath($"{folderPrefix}/{fileName}").ToRootedPath();

        _resource.UserData.CreateDir(resPath.Directory);

        using var stream = _resource.UserData.OpenWrite(resPath);

        stream.Write(content);

        return resPath;
    }
}

[Access(typeof(VirtualDisksManager))]
public sealed class VirtualDisk
{
    [ViewVariables] public readonly string Path;

    [ViewVariables] public readonly int Size;

    public VirtualDisk(string path, int size)
    {
        Path = path;
        Size = size;

        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            PhysSize = (int)info.Length;

            return;
        }

        File.Create(Path).Dispose();
    }

    [ViewVariables] public int PhysSize { get; private set; }

    [PublicAPI]
    [Access(Other = AccessPermissions.ReadWriteExecute)]
    public void Read(Span<byte> target, long offset)
    {
        using var f = File.OpenRead(Path);

        f.Seek(offset, SeekOrigin.Begin);
        f.ReadToEnd(target);
    }

    [PublicAPI]
    [Access(Other = AccessPermissions.ReadWriteExecute)]
    public void Write(Span<byte> source, long offset)
    {
        using (var f = File.OpenWrite(Path))
        {
            var currentPos = f.Seek(offset, SeekOrigin.Begin);

            if (currentPos != offset)
                f.Write(new byte[offset - currentPos]);

            f.Write(source);
        }

        var info = new FileInfo(Path);
        PhysSize = (int)info.Length;
    }
}
