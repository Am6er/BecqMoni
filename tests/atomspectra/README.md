# AtomSpectra test suite and mock board

Tests for the AtomSpectra input device of BecqMoni (the TCP bridge and the REST
client). They run against a mock board on `127.0.0.1`; no real board and no
network access are needed.

## Layout

| Path | Contents |
|---|---|
| `rest_suite.py` | Driver of the REST suite: builds the harness, starts the mock, runs the scenarios, runs the mutations. |
| `run_review_tests.py` | Driver of the TCP suite (session and calibration scenarios, TCP mutations). It also runs `rest_suite.py` at the end. |
| `run_mutations_subset.py` | Runs the REST suite with only the named source mutations. |
| `harness/*.cs` | Test harnesses compiled against `BecquerelMonitor.exe` (`RestHarness*.cs` for REST, `SessionHarness.cs` and `CalHarness.cs` for TCP). |
| `mocks/*.py` | Mock boards: `mock_rest.py` (REST), `mock_cmd.py` (TCP command bridge), `mock_board.py` and `mock_cal.py` (TCP framing and calibration). |
| `fixtures/` | Recorded board responses read by the mocks and the harness: `responses/` (cases for `mock_rest.py --cases`) and `spectrum_sample.json`. |

## Requirements

* Windows with the .NET Framework build tools: MSBuild and the Roslyn `csc.exe`
  (Visual Studio 2022 of any edition, or its Build Tools). The environment variables
  `ATOMSPECTRA_MSBUILD` and `ATOMSPECTRA_CSC` name them; without them the suites ask `vswhere`
  (`%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe -latest -products *
  -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) and take `csc.exe` from
  `Roslyn\` next to that MSBuild; the last fallback is the Build Tools location
  `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\...`.
* Python 3 (the suite was run with Python 3.14; only the standard library is used).
* Set `PYTHONIOENCODING=utf-8` when running from a console with a non-UTF-8 code page.

## Running

1. Build the application in Release configuration (from the repository root), into directories
   of its own:

   ```
   MSBuild.exe BecquerelMonitor\BecquerelMonitor.csproj /restore /t:Build /p:Configuration=Release /p:Platform=AnyCPU
     /p:SignManifests=false /p:GenerateManifests=false
     /p:OutputPath=bin\Release_atomspectra_tests\ /p:IntermediateOutputPath=obj\Release_atomspectra_tests\
   ```

   (one command line; it is split here for reading). Both directories are needed, not only
   `OutputPath`: `/p:GenerateManifests=false` embeds `app.manifest` into the exe, and such an exe
   left in the shared `obj\Release\` is taken as up to date by a ClickOnce Publish from the IDE —
   the update then fails with `RefDefValidation`. A release for the autoupdate is built
   without this key, by the IDE.

   The suite takes two arguments: the directory with the built
   `BecquerelMonitor.exe` and the project source directory
   (`BecquerelMonitor/`, the mutations patch copies of its `.cs` files).

2. Run the REST suite. By default it runs all scenarios on the real build and on a build from the
   sources, then ALL source mutations (117, many hours) and the 10 real-build mutations. Set
   `REST_NO_SOURCE_MUTATIONS=1` to run the scenarios and the real-build mutations only:

   ```
   set REST_NO_SOURCE_MUTATIONS=1
   python rest_suite.py <exeDir> <srcDir>
   ```

   Example: `python rest_suite.py BecquerelMonitor/bin/Release_atomspectra_tests BecquerelMonitor`.
   The work directory defaults to `%TEMP%\atomspectra_tests`; set `REST_WORK` to use another one
   (put it on a local disk).

3. Run only some scenarios: `python rest_suite.py <exeDir> <srcDir> <workDir> --only name1,name2`
   (scenario names are listed in `SCENARIOS` in `rest_suite.py`; set `REST_ONLY_SRC=1` to run
   them on the source build as well).

4. Run the TCP suite (the same two arguments; the REST suite is run at its end, so set
   `REST_NO_SOURCE_MUTATIONS=1` to keep the run short):

   ```
   python run_review_tests.py <exeDir> <srcDir>
   ```

   The work directory is `%TEMP%\atomspectra_tests_tcp` (or `REST_WORK`); its REST part uses
   `%TEMP%\atomspectra_rest_work`.

## What is in the suites

* REST suite: 155 scenarios in `SCENARIOS`, each run on the real build and on a build from the
  sources. A scenario that is only possible on the real build (controller and forms, found by
  reflection) prints `n/a` on the source build and must execute a minimum number of checks
  (`REAL_ONLY_MIN_CHECKS`). The real build speaks the language of the machine (English or
  Russian), so a check of a text that comes from the resources accepts both. The source build
  compiles the client sources with a stub of `Properties.Resources` (the English values of
  `Resources.resx`) and of `AppUi`.
* TCP suite: 12 mock scenarios (real build and source build).
* Mutations show that the tests can fail. Each mutation patches a copy of the client source (the
  `(file, old text, new text)` triples in `rest_suite.py` and `run_review_tests.py`) and expects
  its target scenario to turn red:
  * `MUTATIONS` in `rest_suite.py`: 117 source mutations (a full run takes many hours; select some
    with `python run_mutations_subset.py <exeDir> <srcDir> <workDir> id1,id2`).
  * `REAL_MUTATIONS` in `rest_suite.py`: 10 mutations of the real build, run by default.
  * `run_review_tests.py`: 7 TCP mutations.
  * The `old text` of a mutation must match the source exactly. If a source file changes, a
    mutation whose text no longer matches fails to build and has to be updated.
