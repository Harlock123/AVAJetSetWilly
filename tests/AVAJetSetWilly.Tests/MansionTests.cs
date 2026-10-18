using AVAJetSetWilly.Engine;

namespace AVAJetSetWilly.Tests;

public class MansionTests
{
    private static readonly Mansion H = MansionBook.House;

    [Fact]
    public void Six_rooms_across_and_four_floors_with_doorways_between_neighbours()
    {
        Assert.Equal(Field.HouseCols * Field.Floors, H.Rooms.Length);
        foreach (var r in H.Rooms)
        {
            Assert.Equal(r.HX > 0 ? r.Index - 1 : -1, r.Left);
            Assert.Equal(r.HX < Field.HouseCols - 1 ? r.Index + 1 : -1, r.Right);
            for (int row = Field.DoorTop; row <= Field.DoorBottom; row++)
            {
                Assert.Equal(r.Left >= 0 ? Tile.Empty : Tile.Wall, r.Grid[0, row]);
                Assert.Equal(r.Right >= 0 ? Tile.Empty : Tile.Wall, r.Grid[Field.Cols - 1, row]);
            }
        }
    }

    [Fact]
    public void Every_floor_is_joined_to_the_next_by_a_gap_that_matches_on_both_sides()
    {
        for (int f = 0; f < Field.Floors - 1; f++)
            Assert.Contains(Enumerable.Range(0, Field.HouseCols), x => H.Room(f, x).Down >= 0);
        foreach (var up in H.Rooms.Where(r => r.Down >= 0))
        {
            var down = H.Rooms[up.Down];
            Assert.Equal(up.Index, down.Up);
            Assert.Equal(up.Floor + 1, down.Floor);
            Assert.Equal(up.DownCol, down.UpCol);
            for (int c = up.DownCol - 1; c <= up.DownCol + 3; c++) Assert.Equal(up.Grid[c, Field.Rows - 1], down.Grid[c, 0]);
            Assert.Equal(Tile.Empty, up.Grid[up.DownCol, Field.Rows - 1]);
            Assert.Equal(Tile.Floor, up.Grid[up.DownCol + 1, Field.Rows - 1]);
        }
    }

    [Fact]
    public void Every_room_but_the_master_bedroom_has_items_hanging_in_open_squares()
    {
        foreach (var r in H.Rooms)
        {
            if (r.Bedroom) { Assert.Empty(r.Items); continue; }
            Assert.InRange(r.Items.Count, 2, 3);
            foreach (int i in r.Items)
            {
                var it = H.Items[i];
                Assert.Equal(r.Index, it.Room);
                Assert.Equal(Tile.Empty, r.Grid[it.Col, it.Row]);
            }
        }
        Assert.Equal(H.Rooms.Sum(r => r.Items.Count), H.Items.Count);
    }

    [Fact]
    public void In_every_room_every_way_in_and_every_item_leads_to_every_other()
    {
        foreach (var r in H.Rooms)
        {
            var g = Graph.Build(H, r);
            var targets = r.Items.Select(i => new Spot(H.Items[i].Col, H.Items[i].Row + 1)).Concat(MansionBook.Ways(H, r)).ToList();
            foreach (var t in targets) Assert.True(targets.All(g.Reach(t).Contains), $"{r.Theme.Name}: stuck from {t}");
        }
    }

    [Fact]
    public void Willy_starts_in_the_bathroom_and_his_bed_is_in_the_master_bedroom()
    {
        Assert.Equal("THE BATHROOM", H.Rooms[H.StartRoom].Theme.Name);
        Assert.Equal("THE MASTER BEDROOM", H.Rooms[H.Bedroom].Theme.Name);
        Assert.True(H.Rooms[H.Bedroom].Bedroom);
        Assert.Equal(H.Rooms[H.StartRoom].Right, H.Bedroom);
        Assert.True(H.MariaX < H.BedCol0 * Field.Tile);
    }

    [Fact]
    public void Every_room_has_a_name_of_its_own()
    {
        Assert.Equal(H.Rooms.Length, H.Rooms.Select(r => r.Theme.Name).Distinct().Count());
    }
}
