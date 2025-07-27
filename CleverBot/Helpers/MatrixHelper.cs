using LibMatrix.RoomTypes;

namespace CleverBot.Helpers;

public static class MatrixHelper
{
    public const string ThoughtsRoomId = "!bRQNfEcHgLvdeTaUHT:matrix.org";
    public static IEnumerable<GenericRoom> FilterRooms(this IEnumerable<GenericRoom> rooms)
    {
        return rooms.Where(r => r.RoomId != ThoughtsRoomId);
    }
    
    public static async Task<IEnumerable<GenericRoom>> FilterRoomsAsync<T>(this Task<T> rooms) where T : IEnumerable<GenericRoom>
    {
        return (await rooms).Where(r => r.RoomId != ThoughtsRoomId);
    }
    
}