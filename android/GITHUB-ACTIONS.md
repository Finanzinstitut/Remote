# GitHub Actions

The workflow builds the debug APK and uploads it as `PCRemote-debug-APK`.

The Android project must contain the Gradle wrapper:
`gradlew`, `gradlew.bat`, and `gradle/wrapper/gradle-wrapper.properties`.
Without these files GitHub Actions cannot execute `./gradlew`.
