# -*- mode: python ; coding: utf-8 -*-

block_cipher = None

# Collect all Python source files
a = Analysis(
    ['run_all_syncs.py'],
    pathex=[],
    binaries=[],
    datas=[
        # Include the entire src package
        ('src', 'src'),
        # Include standalone scripts
        ('cleanup_removed_movies.py', '.'),
        ('sync_library_to_watchlist.py', '.'),
        # Include .env template (user needs to edit)
        ('.env', '.'),
    ],
    hiddenimports=[
        'plexapi',
        'pyarr',
        'structlog',
        'httpx',
        'colorama',
        'dotenv',
        'tmdbv3api',
        'sqlite3',
        'src.main',
        'cleanup_removed_movies',
        'sync_library_to_watchlist',
    ],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.zipfiles,
    a.datas,
    [],
    name='vod-filter-complete',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=True,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon=None,
)
