plugins {
    `java-library`
    kotlin("jvm")
}

kotlin {
    jvmToolchain(21)
}

dependencies {
    api("com.badlogicgames.gdx:gdx:1.14.1")
}
