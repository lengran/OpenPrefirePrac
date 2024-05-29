# /usr/bin/python3
import argparse

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("pos_file_path", help="Path to a file that contains positions obtained from in-game getpos command.", type=str)

    args = parser.parse_args()

    with open(args.pos_file_path) as file:
        for line in file:
            words = line.split()
            # Targets
            if len(words) > 6:
                height = float(words[3][:-7])
                height -=64
                crunching = False
                if len(words) == 7:
                    print(words[1] + " " + words[2] + " " + str(height) + " " + words[4] + " " + words[5] + " " + words[6] + " False")
                else:
                    # a spawn point either indicates bot crounching or contains comments
                    line_to_print = words[1] + " " + words[2] + " " + str(height) + " " + words[4] + " " + words[5] + " " + words[6]
                    if words[7].lower() == "false":
                        line_to_print = line_to_print + " False"
                        if len(words) > 8:
                            line_to_print = line_to_print + " #"
                    elif words[7].lower() == "true":
                        line_to_print = line_to_print + " True"
                        if len(words) > 8:
                            line_to_print = line_to_print + " #"
                    else:
                        line_to_print = line_to_print + " False # " + words[7]
                    for i in range(8, len(words)):
                        line_to_print = line_to_print + " " + words[i]

                    print(line_to_print)
            
            # Joints of guiding line
            if len(words) == 4:
                height = float(words[3])
                height -=55
                print(words[1] + " " + words[2] + " " + str(height))

# A note in the end.
# player height = 64
# player crouch height = stand height - 18