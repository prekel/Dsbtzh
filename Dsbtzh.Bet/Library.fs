namespace Dsbtzh.Bet

type UserId = UserId of uint64

type Bet = { Author: UserId; Cost: decimal }

type Variant = { Name: string; Bets: Bet list }

type Prediction = { Variants: Variant list }

module Predictions =
    let pot var =
        var.Bets |> List.map (fun b -> b.Cost) |> List.sum

    let sumPot pred =
        pred.Variants |> List.map pot |> List.sum

    let coef pred winVar = sumPot pred / pot winVar

    let winBet pred winVar bet = bet.Author, bet.Cost * coef pred winVar

    let winBets pred winVar =
        winVar.Bets |> List.map (winBet pred winVar)
