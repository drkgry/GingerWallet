<p align="center">
  <a href="https://gingerwallet.io">
    <img src="https://github.com/GingerPrivacy/GingerWallet/blob/master/ui-ww.png">
  </a>
</p>

<h3 align="center">
    An open-source, non-custodial, privacy-focused Bitcoin wallet for desktop.
</h3>

<h3 align="center">
  <a href="https://gingerwallet.io">
    Website
  </a>
  <span> | </span>
  <a href="https://docs.gingerwallet.io/">
    Documentation
  </a>
  <span> | </span>
  <a href="https://api.gingerwallet.io/swagger">
    API
  </a>
  <span> | </span>
  <a href="https://github.com/GingerPrivacy/GingerWallet/discussions/5185">
    Support
  </a>
  <span> | </span>
  <a href="https://github.com/GingerPrivacy/GingerWallet/blob/master/PGP.txt">
    PGP
  </a>
</h3>

<h3>

| Code Quality           | Windows Tests           | Linux Tests             | macOS Tests             | Continuous Delivery       | Deterministic builds      | License                   |
| :----------------------| :-----------------------| :-----------------------| :-----------------------| :-------------------------| :-------------------------| :-------------------------|
| [![CodeFactor][9]][10] | [![Build Status][1]][2] | [![Build Status][3]][4] | [![Build Status][5]][6] | [![Build Status][11]][12] | [![Build Status][13]][14] | [![GitHub license][7]][8] |

[1]: https://dev.azure.com/zkSNACKs/Wasabi/_apis/build/status/Wasabi.Windows?branchName=master
[2]: https://dev.azure.com/zkSNACKs/Wasabi/_build?definitionId=3
[3]: https://dev.azure.com/zkSNACKs/Wasabi/_apis/build/status/Wasabi.Linux?branchName=master
[4]: https://dev.azure.com/zkSNACKs/Wasabi/_build?definitionId=1
[5]: https://dev.azure.com/zkSNACKs/Wasabi/_apis/build/status/Wasabi.Osx?branchName=master
[6]: https://dev.azure.com/zkSNACKs/Wasabi/_build?definitionId=2
[7]: https://img.shields.io/github/license/GingerPrivacy/GingerWallet.svg
[8]: https://github.com/GingerPrivacy/GingerWallet/blob/master/LICENSE.md
[9]: https://www.codefactor.io/repository/github/zksnacks/walletwasabi/badge
[10]: https://www.codefactor.io/repository/github/zksnacks/walletwasabi
[11]: https://dev.azure.com/zkSNACKs/Wasabi/_apis/build/status/Wasabi.ContinuousDelivery?branchName=master
[12]: https://dev.azure.com/zkSNACKs/Wasabi/_build/latest?definitionId=12&branchName=master
[13]: https://dev.azure.com/zkSNACKs/Wasabi/_apis/build/status/Wasabi.DeterministicBuild?branchName=master
[14]: https://dev.azure.com/zkSNACKs/Wasabi/_build/latest?definitionId=13&branchName=master

</h3>
<br>

# [Download GingerWallet](https://github.com/GingerPrivacy/GingerWallet/releases)

<br>

# Build From Source Code

### Get The Requirements

1. Get Git: https://git-scm.com/downloads
2. Get .NET 8.0 SDK: https://dotnet.microsoft.com/download
3. Optionally disable .NET's telemetry by executing in the terminal `export DOTNET_CLI_TELEMETRY_OPTOUT=1` on Linux and macOS or `setx DOTNET_CLI_TELEMETRY_OPTOUT 1` on Windows.

### Get GingerWallet

Clone & Restore & Build

```sh
git clone --depth=1 --single-branch --branch=master https://github.com/GingerPrivacy/GingerWallet.git
cd WalletWasabi/WalletWasabi.Fluent.Desktop
dotnet build
```

### Run GingerWallet

Run GingerWallet with `dotnet run` from the `WalletWasabi.Fluent.Desktop` folder.

### Update GingerWallet

```sh
git pull
```
