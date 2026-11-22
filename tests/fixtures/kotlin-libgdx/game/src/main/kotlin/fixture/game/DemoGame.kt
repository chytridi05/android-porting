package fixture.game

import com.badlogic.gdx.ApplicationAdapter
import com.badlogic.gdx.Gdx
import com.badlogic.gdx.Input.Keys
import com.badlogic.gdx.graphics.Color
import com.badlogic.gdx.graphics.g2d.BitmapFont
import com.badlogic.gdx.graphics.g2d.SpriteBatch
import com.badlogic.gdx.graphics.glutils.ShapeRenderer
import com.badlogic.gdx.math.Vector2
import com.badlogic.gdx.utils.ScreenUtils
import com.badlogic.gdx.utils.viewport.FitViewport

open class DemoGame : ApplicationAdapter() {
    private val maze = listOf(".#.#.", ".#...", ".###.", "....G")
    private val viewport = FitViewport(480f, 360f)
    private lateinit var shapes: ShapeRenderer
    private lateinit var batch: SpriteBatch
    private lateinit var font: BitmapFont
    private lateinit var title: String
    private var column = 0
    private var row = 0
    private var won = false

    override fun create() {
        shapes = ShapeRenderer()
        batch = SpriteBatch()
        font = BitmapFont()
        title = Gdx.files.internal("title.txt").readString("UTF-8").trim()
        Gdx.graphics.setTitle(title)
    }

    override fun resize(width: Int, height: Int) {
        viewport.update(width, height, true)
    }

    override fun render() {
        handleInput()
        ScreenUtils.clear(0.08f, 0.11f, 0.15f, 1f)
        viewport.apply()
        shapes.projectionMatrix = viewport.camera.combined
        shapes.begin(ShapeRenderer.ShapeType.Filled)
        maze.forEachIndexed { y, line ->
            line.forEachIndexed { x, tile ->
                shapes.color = when (tile) {
                    '#' -> Color.DARK_GRAY
                    'G' -> Color.FOREST
                    else -> Color.SLATE
                }
                shapes.rect(BoardX + x * TileSize, BoardY + y * TileSize, TileSize - 2, TileSize - 2)
            }
        }
        shapes.color = Color.GOLD
        shapes.circle(BoardX + (column + 0.5f) * TileSize, BoardY + (row + 0.5f) * TileSize, 15f)
        shapes.end()

        batch.projectionMatrix = viewport.camera.combined
        batch.begin()
        font.draw(batch, if (won) "YOU WIN - Enter or tap to replay" else "Reach the green tile", BoardX, 335f)
        font.draw(batch, "Arrow keys or tap a neighboring tile", BoardX, 40f)
        batch.end()
    }

    override fun dispose() {
        font.dispose()
        batch.dispose()
        shapes.dispose()
    }

    private fun handleInput() {
        if (won) {
            if (Gdx.input.isKeyJustPressed(Keys.ENTER) || Gdx.input.justTouched()) {
                column = 0
                row = 0
                won = false
                Gdx.graphics.setTitle(title)
            }
            return
        }

        when {
            Gdx.input.isKeyJustPressed(Keys.LEFT) -> move(column - 1, row)
            Gdx.input.isKeyJustPressed(Keys.RIGHT) -> move(column + 1, row)
            Gdx.input.isKeyJustPressed(Keys.DOWN) -> move(column, row - 1)
            Gdx.input.isKeyJustPressed(Keys.UP) -> move(column, row + 1)
            Gdx.input.justTouched() -> {
                val point = viewport.unproject(Vector2(Gdx.input.x.toFloat(), Gdx.input.y.toFloat()))
                val x = kotlin.math.floor((point.x - BoardX) / TileSize).toInt()
                val y = kotlin.math.floor((point.y - BoardY) / TileSize).toInt()
                if (kotlin.math.abs(x - column) + kotlin.math.abs(y - row) == 1) move(x, y)
            }
        }
    }

    private fun move(x: Int, y: Int) {
        if (y !in maze.indices || x !in maze[y].indices || maze[y][x] == '#') return

        column = x
        row = y
        if (maze[row][column] == 'G') {
            won = true
            Gdx.graphics.setTitle("$title - YOU WIN")
        }
    }

    private companion object {
        const val BoardX = 90f
        const val BoardY = 64f
        const val TileSize = 60f
    }
}

class AlternateGame : DemoGame()
