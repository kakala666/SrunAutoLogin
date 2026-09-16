import Foundation

if CommandLine.arguments.contains("--selftest") {
    SrunCrypto.selfTest()
    exit(0)
}
SrunApp.main()
