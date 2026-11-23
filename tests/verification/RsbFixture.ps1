function Get-RsbFixtureResources {
    return [ordered] @{
        'levels/route.txt' = [Text.Encoding]::ASCII.GetBytes('RUR')
        'levels/hint.txt' = [Text.Encoding]::ASCII.GetBytes('Use the left and right halves')
        'fixture.ptx' = [byte[]] @(0x40, 0x90, 0x00, 0xFF)
    }
}

function New-RsbFixture([string] $Path, [uint32] $Flags = 3) {
    $resources = Get-RsbFixtureResources
    $route = $resources['levels/route.txt']
    $hint = $resources['levels/hint.txt']
    $texture = $resources['fixture.ptx']

    $directory = [IO.MemoryStream]::new()
    $index = [IO.BinaryWriter]::new($directory)
    try {
        foreach ($letter in 'levels\route.txt'.ToCharArray()) {
            $index.Write([uint32] $letter)
        }
        foreach ($field in @(0, 0, 0, $route.Length)) {
            $index.Write([uint32] $field)
        }

        # The branch at the first divergent letter reuses the common directory prefix.
        $hintNode = $directory.Position / 4
        foreach ($letter in 'hint.txt'.ToCharArray()) {
            $index.Write([uint32] $letter)
        }
        foreach ($field in @(0, 0, $route.Length, $hint.Length)) {
            $index.Write([uint32] $field)
        }
        $textureNode = $directory.Position / 4
        foreach ($letter in 'fixture.ptx'.ToCharArray()) {
            $index.Write([uint32] $letter)
        }
        foreach ($field in @(0, 1, 0, $texture.Length, 0, 0, 0, 1, 1)) {
            $index.Write([uint32] $field)
        }
        $dictionary = $directory.ToArray()
        [BitConverter]::GetBytes([uint32] (($textureNode -shl 8) -bor [uint32] [char] 'l')).CopyTo($dictionary, 0)
        [BitConverter]::GetBytes([uint32] (($hintNode -shl 8) -bor [uint32] [char] 'r')).CopyTo($dictionary, 7 * 4)
    }
    finally {
        $index.Dispose()
    }

    $packetOffset = 0x1000
    $dataOffset = 0x1000
    $sections = @(
        @{ Bytes = [byte[]] ($route + $hint); Flag = 2; Field = 0x18 }
        @{ Bytes = $texture; Flag = 1; Field = 0x28 }
    )
    foreach ($section in $sections) {
        $section.Stored = $section.Bytes
        if ($Flags -band $section.Flag) {
            $compressed = [IO.MemoryStream]::new()
            try {
                $zlib = [IO.Compression.ZLibStream]::new($compressed, [IO.Compression.CompressionMode]::Compress, $true)
                try { $zlib.Write($section.Bytes) } finally { $zlib.Dispose() }
                $section.Stored = $compressed.ToArray()
            }
            finally { $compressed.Dispose() }
        }
        $section.Offset = $dataOffset
        $dataOffset += $section.Stored.Length
    }

    $bundle = [byte[]]::new($packetOffset + $dataOffset)
    [Text.Encoding]::ASCII.GetBytes('1bsr').CopyTo($bundle, 0)
    foreach ($field in @(@(4, 4), @(0x0C, $packetOffset), @(0x28, 1), @(0x2C, 0x70), @(0x30, 0xCC), @(0x6C, $packetOffset))) {
        [BitConverter]::GetBytes([uint32] $field[1]).CopyTo($bundle, $field[0])
    }
    [Text.Encoding]::ASCII.GetBytes('FIXTURE').CopyTo($bundle, 0x70)
    [BitConverter]::GetBytes([uint32] $packetOffset).CopyTo($bundle, 0x70 + 0x80)
    [BitConverter]::GetBytes([uint32] $dataOffset).CopyTo($bundle, 0x70 + 0x84)

    [Text.Encoding]::ASCII.GetBytes('pgsr').CopyTo($bundle, $packetOffset)
    foreach ($field in @(@(4, 4), @(0x10, $Flags), @(0x14, 0x1000), @(0x48, $dictionary.Length), @(0x4C, 0x5C))) {
        [BitConverter]::GetBytes([uint32] $field[1]).CopyTo($bundle, $packetOffset + $field[0])
    }
    $dictionary.CopyTo($bundle, $packetOffset + 0x5C)
    foreach ($section in $sections) {
        $field = $packetOffset + $section.Field
        [BitConverter]::GetBytes([uint32] $section.Offset).CopyTo($bundle, $field)
        [BitConverter]::GetBytes([uint32] $section.Stored.Length).CopyTo($bundle, $field + 4)
        [BitConverter]::GetBytes([uint32] $section.Bytes.Length).CopyTo($bundle, $field + 8)
        $section.Stored.CopyTo($bundle, $packetOffset + $section.Offset)
    }
    [IO.File]::WriteAllBytes($Path, $bundle)
}
