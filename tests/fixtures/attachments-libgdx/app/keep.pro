-dontshrink
-dontoptimize
-dontwarn **
-keep class com.badlogic.gdx.** { *; }
-keep class fixture.attachment.android.** { *; }
-keepclassmembers class fixture.attachment.RouteGame {
    public <init>();
    public void create();
    public void render();
}
