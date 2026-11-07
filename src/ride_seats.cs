using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace camera_pov
{
    // An attraction's seats, grouped by car: each car of each train of a tracked ride, each boat of a boat ride, or a
    // single group with all of a flat ride's seats. Built again whenever it's needed, since trains and boats come and
    // go as the ride is edited.
    internal static class ride_seats
    {
        // FlatRide.seats is protected.
        private static readonly FieldInfo flat_ride_seats_field = typeof(FlatRide).GetField("seats", BindingFlags.Instance | BindingFlags.NonPublic);

        public static List<List<Seat>> cars_of(Attraction attraction)
        {
            List<List<Seat>> cars = new List<List<Seat>>();
            switch (attraction)
            {
                case TrackedRide tracked_ride when tracked_ride.Track != null:
                    foreach (Train train in tracked_ride.Track.trains)
                    {
                        if (train == null)
                        {
                            continue;
                        }
                        foreach (Car car in train.cars)
                        {
                            add(cars, car != null ? car.seats : null);
                        }
                    }
                    break;
                case BoatRide boat_ride:
                    foreach (Boat boat in boat_ride.boats)
                    {
                        add(cars, boat != null ? boat.seats : null);
                    }
                    break;
                case FlatRide flat_ride:
                    add(cars, flat_ride_seats_field != null ? flat_ride_seats_field.GetValue(flat_ride) as IEnumerable : null);
                    break;
            }
            return cars;
        }

        // Only seats that have been placed in the world, which is where the camera goes.
        private static void add(List<List<Seat>> cars, IEnumerable seats)
        {
            if (seats == null)
            {
                return;
            }
            List<Seat> car = new List<Seat>();
            foreach (object seat in seats)
            {
                if (seat is Seat placed && placed.transform != null)
                {
                    car.Add(placed);
                }
            }
            if (car.Count > 0)
            {
                cars.Add(car);
            }
        }
    }
}
