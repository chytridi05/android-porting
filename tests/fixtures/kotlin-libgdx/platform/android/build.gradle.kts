plugins {
    id("com.android.application")
    kotlin("android")
}

android {
    namespace = "fixture.android"
    compileSdk = 36

    defaultConfig {
        applicationId = "fixture.android"
        minSdk = 21
        targetSdk = 36
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_21
        targetCompatibility = JavaVersion.VERSION_21
    }

    sourceSets.named("main") {
        assets.setSrcDirs(listOf("../../assets/default", "../../assets/shared"))
    }
}

kotlin {
    jvmToolchain(21)
}

dependencies {
    implementation(project(":shared"))
    implementation("com.badlogicgames.gdx:gdx-backend-android:1.14.1")
}
