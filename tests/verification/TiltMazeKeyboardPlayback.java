import com.badlogic.gdx.ApplicationAdapter;
import com.badlogic.gdx.Gdx;
import com.badlogic.gdx.Input;
import com.badlogic.gdx.Input.Keys;
import com.badlogic.gdx.backends.lwjgl3.Lwjgl3Application;
import com.badlogic.gdx.backends.lwjgl3.Lwjgl3ApplicationConfiguration;
import com.micro1.maze.MazeGame;
import com.micro1.maze.MazeWorld;
import com.micro1.maze.SandboxScreen;
import com.micro1.maze.TitleScreen;
import java.lang.reflect.Field;
import java.lang.reflect.Proxy;

public class TiltMazeKeyboardPlayback extends ApplicationAdapter {
    private static final float[][] WAYPOINTS = {
        {60, 406}, {730, 406}, {730, 280}, {100, 280}, {100, 192},
        {730, 192}, {730, 111}, {100, 111}, {100, 52}, {720, 52}
    };
    private final MazeGame game = new MazeGame();
    private final Field worldField;
    private int waypoint;
    private int frames;
    private int phase;
    private float oldX = 60;
    private float oldY = 390;
    private boolean completed;

    public TiltMazeKeyboardPlayback() throws ReflectiveOperationException {
        worldField = SandboxScreen.class.getDeclaredField("world");
        worldField.setAccessible(true);
    }

    public static void main(String[] args) throws ReflectiveOperationException {
        var config = new Lwjgl3ApplicationConfiguration();
        config.setInitialVisible(false);
        config.setWindowedMode(1280, 720);
        config.setForegroundFPS(60);
        config.useVsync(false);
        var verification = new TiltMazeKeyboardPlayback();
        new Lwjgl3Application(verification, config);
        if (!verification.completed) throw new IllegalStateException("Keyboard replay did not finish");
    }

    @Override public void create() { game.create(); }
    @Override public void resize(int width, int height) { game.resize(width, height); }
    @Override public void dispose() { game.dispose(); }

    @Override
    public void render() {
        if (++frames > 3200) throw new IllegalStateException("Keyboard replay ran out of frames");
        if (game.getScreen() instanceof TitleScreen) {
            drawWithKeys(0, 0, Keys.ENTER);
            if (!(game.getScreen() instanceof SandboxScreen)) throw new IllegalStateException("Enter did not start the game");
            System.out.println("Enter opened the gameplay screen.");
            return;
        }

        MazeWorld world = readWorld();
        var ball = world.getBallPositionPx();
        float x = ball.x, y = ball.y;
        if (world.getState() == MazeWorld.State.FELL_IN) {
            throw new IllegalStateException("Ball fell into a hole at waypoint " + waypoint + " (" + x + ", " + y + ")");
        }
        if (world.getState() == MazeWorld.State.WON) {
            System.out.printf("Reached WON at (%.1f, %.1f) after %d frames.%n", x, y, frames);
            drawWithKeys(0, 0, Keys.ENTER);
            checkReset(world);
            System.out.println("Enter replay reset the game after the win.");
            completed = true;
            Gdx.app.exit();
            return;
        }
        if (phase == 0) {
            if (x < 90) {
                drawWithKeys(1, 0, -1);
            } else {
                drawWithKeys(0, 0, Keys.R);
                checkReset(world);
                System.out.println("Right moved the ball and R returned it to the starting position.");
                phase = 1;
            }
            return;
        }

        float dt = Math.max(0.004f, Math.min(Gdx.graphics.getDeltaTime(), 1f / 30f));
        float vx = (x - oldX) / dt, vy = (y - oldY) / dt;
        oldX = x;
        oldY = y;
        float[] target = WAYPOINTS[waypoint];
        if (waypoint < WAYPOINTS.length - 1 && Math.abs(target[0] - x) < 2.8f
                && Math.abs(target[1] - y) < 2.8f && Math.abs(vx) < 30 && Math.abs(vy) < 30) {
            System.out.printf("Waypoint %d reached at (%.1f, %.1f).%n", waypoint + 1, x, y);
            target = WAYPOINTS[++waypoint];
        }
        drawWithKeys(direction(target[0] - x, vx), direction(target[1] - y, vy), -1);
    }

    private static int direction(float error, float speed) {
        float desired = Math.clamp(5f * error, -220f, 220f);
        float correction = desired - speed;
        return correction > 7 ? 1 : correction < -7 ? -1 : 0;
    }

    private MazeWorld readWorld() {
        try {
            return (MazeWorld) worldField.get(game.getScreen());
        } catch (ReflectiveOperationException exception) {
            throw new IllegalStateException(exception);
        }
    }

    private static void checkReset(MazeWorld world) {
        var position = world.getBallPositionPx();
        if (world.getState() != MazeWorld.State.PLAYING || Math.abs(position.x - 60) > 0.1f
                || Math.abs(position.y - 390) > 0.1f) {
            throw new IllegalStateException("The ball did not reset to the start");
        }
    }

    private void drawWithKeys(int horizontal, int vertical, int pressed) {
        Input hardware = Gdx.input;
        Gdx.input = (Input) Proxy.newProxyInstance(Input.class.getClassLoader(), new Class<?>[] { Input.class },
                (proxy, method, args) -> switch (method.getName()) {
                    case "isKeyJustPressed" -> (int) args[0] == pressed;
                    case "isKeyPressed" -> switch ((int) args[0]) {
                        case Keys.RIGHT -> horizontal > 0;
                        case Keys.LEFT -> horizontal < 0;
                        case Keys.UP -> vertical > 0;
                        case Keys.DOWN -> vertical < 0;
                        default -> false;
                    };
                    default -> method.invoke(hardware, args);
                });
        try {
            game.render();
        } finally {
            Gdx.input = hardware;
        }
    }
}