open Dsbtzh.Bet

module Program =

    [<EntryPoint>]
    let main _ =
        let v1 =
            { Name = "Да"
              Bets =
                  [ { Author = UserId 1UL; Cost = 2M }
                    { Author = UserId 2UL; Cost = 3M } ] }

        let v2 =
            { Name = "Нет"
              Bets =
                  [ { Author = UserId 3UL; Cost = 2M }
                    { Author = UserId 4UL; Cost = 8M }
                    { Author = UserId 5UL; Cost = 10M } ] }

        let pred = { Variants = [ v1; v2 ] }

        let pot1 = Predictions.pot v1
        let pot2 = Predictions.pot v2
        let pot = Predictions.sumPot pred
        let coef = Predictions.coef pred v2
        
        let a = Predictions.winBets pred v2

        let pot1' = Predictions.pot v1
        let pot2' = Predictions.pot v2
        let pot' = Predictions.sumPot pred
        let coef' = Predictions.coef pred v1
        
        let a' = Predictions.winBets pred v1

        0
