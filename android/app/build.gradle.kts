plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

// CI passes -PappVersion=1.1.0 (from the release tag); local builds are 0.0.0-dev.
val appVersion = (findProperty("appVersion") as String?)?.removePrefix("v") ?: "0.0.0-dev"
val versionParts = appVersion.substringBefore('-').split('.').map { it.toIntOrNull() ?: 0 } + listOf(0, 0, 0)

// Release signing comes from CI secrets; without them the APK is debug-signed (fine for testing,
// but Android only installs an update over an APK signed with the same key).
val keystoreFile = System.getenv("ANDROID_KEYSTORE_FILE")

android {
    namespace = "com.jaiscorex.reminderx"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.jaiscorex.reminderx"
        minSdk = 26
        targetSdk = 35
        versionName = appVersion
        versionCode = maxOf(1, versionParts[0] * 10000 + versionParts[1] * 100 + versionParts[2])
    }

    signingConfigs {
        if (keystoreFile != null) {
            create("release") {
                storeFile = file(keystoreFile)
                storePassword = System.getenv("ANDROID_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("ANDROID_KEY_ALIAS")
                keyPassword = System.getenv("ANDROID_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = if (keystoreFile != null) signingConfigs.getByName("release") else signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }
}

dependencies {
    val composeBom = platform("androidx.compose:compose-bom:2024.12.01")
    implementation(composeBom)
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("androidx.compose.ui:ui-tooling-preview")

    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.7")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.8.7")
    implementation("androidx.work:work-runtime-ktx:2.10.0")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.9.0")

    // QR scanner screen (camera + ZXing decoding)
    implementation("com.journeyapps:zxing-android-embedded:4.3.0")
}
