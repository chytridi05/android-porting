package fixture.game

import com.badlogic.gdx.ApplicationAdapter
import com.badlogic.gdx.Gdx
import com.badlogic.gdx.utils.ScreenUtils

open class DemoGame : ApplicationAdapter() {
    override fun create() {
        Gdx.graphics.setTitle(Gdx.files.internal("title.txt").readString("UTF-8").trim())
    }

    override fun render() {
        ScreenUtils.clear(0.12f, 0.66f, 0.64f, 1f)
    }
}

class AlternateGame : DemoGame()
