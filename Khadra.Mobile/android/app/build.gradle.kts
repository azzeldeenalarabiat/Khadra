import java.util.Properties

plugins {
    id("com.android.application")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

// Release builds are signed with Khadra's own key, never with a debug key. The key and its passwords
// stay out of git: android/key.properties (gitignored) names a keystore in the repository's gitignored
// .keys/ folder. See docs/production.md, "The customer app".
//
// Every 1.0.0 build was signed with one laptop's debug key. An APK signed with a different key cannot
// install over the copy on a phone, so a release build that quietly fell back to a debug key would
// produce an app nobody could update. Without key.properties a release build fails instead.
val releaseSigning: Properties? = rootProject.file("key.properties").takeIf { it.exists() }?.let { file ->
    Properties().apply { file.inputStream().use { load(it) } }
}

android {
    namespace = "com.khadra.khadra_mobile"
    compileSdk = flutter.compileSdkVersion
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
        // flutter_local_notifications schedules with java.time, which needs desugaring below API 26.
        isCoreLibraryDesugaringEnabled = true
    }

    defaultConfig {
        // TODO: Specify your own unique Application ID (https://developer.android.com/studio/build/application-id.html).
        applicationId = "com.khadra.khadra_mobile"
        // You can update the following values to match your application needs.
        // For more information, see: https://flutter.dev/to/review-gradle-config.
        minSdk = flutter.minSdkVersion
        targetSdk = flutter.targetSdkVersion
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    // Which Khadra this APK talks to. The flavor is the ONE selector: it picks the Android identity
    // here and, through Flutter's `appFlavor`, the API address in AppEnvironment. See
    // docs/production.md, "The customer app".
    //
    // `production` changes nothing about the app customers have: same applicationId, same name, same
    // key, and the API address is still passed at build time. pubspec.yaml names it the default
    // flavor, so the release command that predates flavors still builds exactly this.
    //
    // `staging` is a different application on the phone (".staging"), so a tester can hold both and
    // neither can ever update over the other. Its name ("Khadra TEST") lives in src/staging/res.
    //
    // NO versionNameSuffix. The API compares the version the app reports against
    // MobileApp:MinimumSupportedVersion, and "1.1.0-staging" is a PRERELEASE that ranks below 1.1.0
    // — the staging build would be refused on every call with 426 and never get past the update
    // screen.
    flavorDimensions += "environment"
    productFlavors {
        create("production") {
            dimension = "environment"
        }
        create("staging") {
            dimension = "environment"
            applicationIdSuffix = ".staging"
        }
    }

    signingConfigs {
        if (releaseSigning != null) {
            create("release") {
                storeFile = rootProject.file(releaseSigning.getProperty("storeFile"))
                storePassword = releaseSigning.getProperty("storePassword")
                keyAlias = releaseSigning.getProperty("keyAlias")
                keyPassword = releaseSigning.getProperty("keyPassword")
            }
        }
    }

    buildTypes {
        release {
            signingConfig = signingConfigs.findByName("release")
        }
    }
}

// Refuses at the start of a release build, not after minutes of compiling, and names the fix.
tasks.matching { it.name == "preReleaseBuild" }.configureEach {
    doFirst {
        if (releaseSigning == null) {
            throw GradleException(
                "A release build needs Khadra's release key, and android/key.properties is missing. " +
                    "See docs/production.md, \"The customer app\". Never sign a release with a debug key: " +
                    "it could not update the app on anybody's phone."
            )
        }
    }
}

kotlin {
    compilerOptions {
        jvmTarget = org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17
    }
}

flutter {
    source = "../.."
}

dependencies {
    coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.5")
}

// Push notifications. Each environment is its OWN Firebase project (owner, 2026-09-23): the
// production app's file goes in src/production/, the staging app's in src/staging/, and neither may
// ever be the other's. The plugin is applied only once a file exists, so a checkout without Firebase
// still builds; the app then runs with push switched off and says so in its log.
if (listOf("production", "staging").any { file("src/$it/google-services.json").exists() }) {
    apply(plugin = "com.google.gms.google-services")
}
